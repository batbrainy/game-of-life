using System.Net;
using System.Net.Http.Json;

using GameOfLife.Api.Endpoints;
using GameOfLife.Api.Persistence;
using GameOfLife.Api.Tests.Endpoints;
using GameOfLife.Core;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace GameOfLife.Api.Tests.Persistence;

[Collection(PostgresFixture.CollectionName)]
public sealed class BoardMutationTests(PostgresFixture postgres)
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(3);
    private static readonly Board Blinker = Board.FromMatrix([[0, 1, 0], [0, 1, 0], [0, 1, 0]]);

    [Fact]
    public async Task SameGuidSerializesAcrossPoolsButAnotherGuidProceeds()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var firstPool = NpgsqlDataSource.Create(connectionString);
        await using var secondPool = NpgsqlDataSource.Create(connectionString);
        var first = Repository(firstPool);
        var second = Repository(secondPool);
        var id = Guid.NewGuid();
        await using (var held = await first.LockAsync(id, WaitLimit, CancellationToken.None))
        {
            await using var other = await second.LockAsync(Guid.NewGuid(), WaitLimit, CancellationToken.None);
            await Assert.ThrowsAsync<BoardLockTimeoutException>(() => second.LockAsync(id, TimeSpan.FromMilliseconds(150), CancellationToken.None));
        }

        await using var acquired = await second.LockAsync(id, WaitLimit, CancellationToken.None);
        Assert.Null(await acquired.FindAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DisposingSessionUnlocksBeforeTheConnectionIsReused()
    {
        var builder = new NpgsqlConnectionStringBuilder(await postgres.CreateMigratedDatabaseAsync()) { MaxPoolSize = 1 };
        await using var pool = NpgsqlDataSource.Create(builder.ConnectionString);
        var repository = Repository(pool);
        int before;
        await using (var initial = await pool.OpenConnectionAsync())
        {
            before = initial.ProcessID;
        }

        await using (var held = await repository.LockAsync(Guid.NewGuid(), WaitLimit, CancellationToken.None))
        {
            Assert.Null(await held.FindAsync(CancellationToken.None));
        }

        await using var reused = await pool.OpenConnectionAsync();
        Assert.Equal(before, reused.ProcessID);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid()", reused);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task CancellationWhileWaitingDoesNotReleaseAnotherSessionsLock()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var firstPool = NpgsqlDataSource.Create(connectionString);
        await using var secondPool = NpgsqlDataSource.Create(connectionString);
        var id = Guid.NewGuid();
        await using var held = await Repository(firstPool).LockAsync(id, WaitLimit, CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Repository(secondPool).LockAsync(id, WaitLimit, cancellation.Token));
        await Assert.ThrowsAsync<BoardLockTimeoutException>(() => Repository(secondPool).LockAsync(id, TimeSpan.FromMilliseconds(100), CancellationToken.None));
    }

    [Fact]
    public async Task CancelledWriteAndInvalidUpdateLeaveAllFieldsUnchangedAndReleaseLock()
    {
        await using var pool = NpgsqlDataSource.Create(await postgres.CreateMigratedDatabaseAsync());
        var repository = Repository(pool);
        var id = Guid.NewGuid();
        await repository.AddAsync(id, Blinker, CancellationToken.None);
        var original = await repository.FindAsync(id, CancellationToken.None);
        await using (var held = await repository.LockAsync(id, WaitLimit, CancellationToken.None))
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => held.SaveAsync(Blinker.Next(), 1, BoardStatus.Active, null, null, cancelled.Token));
            await Assert.ThrowsAsync<PostgresException>(() => held.SaveAsync(Blinker.Next(), 1, BoardStatus.Cycle, null, null, CancellationToken.None));
        }

        AssertSameState(original, await repository.FindAsync(id, CancellationToken.None));
        await using var next = await repository.LockAsync(id, WaitLimit, CancellationToken.None);
        var saved = await next.SaveAsync(Blinker.Next(), 1, BoardStatus.Active, null, null, CancellationToken.None);
        Assert.Equal(1, saved.Generation);
        Assert.Equal(Blinker.Next().ToMatrix(), saved.Board.ToMatrix());
    }

    [Fact]
    public async Task LostSessionCannotWriteAfterAnotherSessionAcquiresTheLock()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = $"lock-test-{Guid.NewGuid():N}" };
        await using var ownerPool = NpgsqlDataSource.Create(builder.ConnectionString);
        await using var adminPool = NpgsqlDataSource.Create(connectionString);
        var owner = Repository(ownerPool);
        var id = Guid.NewGuid();
        await owner.AddAsync(id, Blinker, CancellationToken.None);
        await using var lost = await owner.LockAsync(id, WaitLimit, CancellationToken.None);
        Assert.NotNull(await lost.FindAsync(CancellationToken.None));
        await using (var kill = adminPool.CreateCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = $1"))
        {
            kill.Parameters.AddWithValue(builder.ApplicationName);
            Assert.Equal(true, await kill.ExecuteScalarAsync());
        }

        await using (var replacement = await Repository(adminPool).LockAsync(id, WaitLimit, CancellationToken.None))
        {
            await replacement.SaveAsync(Blinker.Next(), 1, BoardStatus.Active, null, null, CancellationToken.None);
        }

        var error = await Record.ExceptionAsync(() => lost.SaveAsync(Blinker, 2, BoardStatus.Active, null, null, CancellationToken.None));
        Assert.True(error is NpgsqlException or InvalidOperationException);
        var saved = await Repository(adminPool).FindAsync(id, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(1, saved.Generation);
        Assert.Equal(Blinker.Next().ToMatrix(), saved.Board.ToMatrix());
    }

    [Fact]
    public async Task TwoHostsAdvanceTheSameBoardWithoutLosingGenerations()
    {
        await using var factory = new ApiFactory(postgres);
        await factory.InitializeAsync();
        using var first = factory.WithSettings(("GameOfLife:MaxConcurrentSimulations", "8"));
        using var second = factory.WithSettings(("GameOfLife:MaxConcurrentSimulations", "8"));
        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();
        var id = await TestBoards.UploadAsync(firstClient, "[[0,1,0],[0,1,0],[0,1,0]]");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, 8).Select(async index =>
        {
            await start.Task;
            var client = index % 2 == 0 ? firstClient : secondClient;
            using var response = await client.PostAsync($"/api/v1/boards/{id}/next", null);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<BoardStateResponse>())?.Generation;
        }).ToArray();
        start.SetResult();
        var generations = await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(Enumerable.Range(1, 8).Select(n => (long?)n), generations.Order());
        var state = await firstClient.GetFromJsonAsync<BoardStateResponse>($"/api/v1/boards/{id}");
        Assert.NotNull(state);
        Assert.Equal(8, state.Generation);
    }

    [Fact]
    public async Task NextAndFinalAcrossHostsCannotOverwriteTerminalState()
    {
        await using var factory = new ApiFactory(postgres);
        await factory.InitializeAsync();
        using var second = factory.WithSettings();
        using var firstClient = factory.CreateClient();
        using var secondClient = second.CreateClient();
        var id = await TestBoards.UploadAsync(firstClient, "[[0,1,0],[0,1,0],[0,1,0]]");
        var nextTask = firstClient.PostAsync($"/api/v1/boards/{id}/next", null);
        var finalTask = secondClient.PostAsync($"/api/v1/boards/{id}/final", null);
        using var next = await nextTask;
        using var final = await finalTask;
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        Assert.True(next.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
        var state = await secondClient.GetFromJsonAsync<BoardStateResponse>($"/api/v1/boards/{id}");
        Assert.NotNull(state);
        Assert.Equal("Cycle", state.Status);
        Assert.Equal(next.StatusCode == HttpStatusCode.OK ? 3 : 2, state.Generation);
        using var rejected = await firstClient.PostAsync($"/api/v1/boards/{id}/next", null);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
    }

    [Fact]
    public async Task HeldMutationLockDoesNotBlockReadsProjectionsOrAnotherBoard()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var pool = NpgsqlDataSource.Create(connectionString);
        var repository = Repository(pool);
        var id = Guid.NewGuid();
        await repository.AddAsync(id, Blinker, CancellationToken.None);
        await using var factory = new ApiFactory(postgres);
        using var configured = factory.WithSettings(("ConnectionStrings:GameOfLife", connectionString), ("GameOfLife:BoardLockTimeoutSeconds", "1"));
        using var client = configured.CreateClient();
        await using var held = await repository.LockAsync(id, WaitLimit, CancellationToken.None);
        using var fetched = await client.GetAsync($"/api/v1/boards/{id}").WaitAsync(WaitLimit);
        using var projected = await client.GetAsync($"/api/v1/boards/{id}/generations/1").WaitAsync(WaitLimit);
        var otherId = await TestBoards.UploadAsync(client, "[[1]]");
        using var other = await client.PostAsync($"/api/v1/boards/{otherId}/next", null).WaitAsync(WaitLimit);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(HttpStatusCode.OK, projected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        using var blocked = await client.PostAsync($"/api/v1/boards/{id}/next", null).WaitAsync(WaitLimit);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), blocked.Headers.RetryAfter?.Delta);
        var original = await repository.FindAsync(id, CancellationToken.None);
        Assert.NotNull(original);
        Assert.Equal(0, original.Generation);
    }

    [Fact]
    public async Task FinalReplayDoesNotChangeTimestampsAndGenerationOverflowIsRejected()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var pool = NpgsqlDataSource.Create(connectionString);
        await using var factory = new ApiFactory(postgres);
        using var configured = factory.WithSettings(("ConnectionStrings:GameOfLife", connectionString));
        using var client = configured.CreateClient();
        var id = await TestBoards.UploadAsync(client, "[[1,1],[1,1]]");
        using var first = await client.PostAsync($"/api/v1/boards/{id}/final", null);
        first.EnsureSuccessStatusCode();
        var before = await Repository(pool).FindAsync(id, CancellationToken.None);
        using var again = await client.PostAsync($"/api/v1/boards/{id}/final", null);
        again.EnsureSuccessStatusCode();
        AssertSameState(before, await Repository(pool).FindAsync(id, CancellationToken.None));
        var activeId = await TestBoards.UploadAsync(client, "[[1]]");
        await using var command = pool.CreateCommand("UPDATE boards SET generation = 9223372036854775807 WHERE id = $1");
        command.Parameters.AddWithValue(activeId);
        await command.ExecuteNonQueryAsync();
        using var next = await client.PostAsync($"/api/v1/boards/{activeId}/next", null);
        using var projection = await client.GetAsync($"/api/v1/boards/{activeId}/generations/1");
        Assert.Equal(HttpStatusCode.Conflict, next.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, projection.StatusCode);
    }

    [Theory]
    [InlineData("[[1,1],[1,1]]", long.MaxValue - 1, HttpStatusCode.OK)]
    [InlineData("[[1]]", long.MaxValue - 1, HttpStatusCode.Conflict)]
    [InlineData("[[1,1],[1,1]]", long.MaxValue, HttpStatusCode.Conflict)]
    public async Task FinalChecksTheComputedStepsForGenerationOverflow(string cells, long generation, HttpStatusCode expected)
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var pool = NpgsqlDataSource.Create(connectionString);
        await using var factory = new ApiFactory(postgres);
        using var configured = factory.WithSettings(("ConnectionStrings:GameOfLife", connectionString));
        using var client = configured.CreateClient();
        var id = await TestBoards.UploadAsync(client, cells);
        await using (var command = pool.CreateCommand("UPDATE boards SET generation = $1 WHERE id = $2"))
        {
            command.Parameters.AddWithValue(generation);
            command.Parameters.AddWithValue(id);
            await command.ExecuteNonQueryAsync();
        }

        var before = await Repository(pool).FindAsync(id, CancellationToken.None);
        using var response = await client.PostAsync($"/api/v1/boards/{id}/final", null);
        Assert.Equal(expected, response.StatusCode);
        var after = await Repository(pool).FindAsync(id, CancellationToken.None);
        if (expected == HttpStatusCode.OK)
        {
            Assert.NotNull(after);
            Assert.Equal(long.MaxValue, after.Generation);
            Assert.Equal(generation, after.CycleStartGeneration);
            Assert.Equal(BoardStatus.Stable, after.Status);
            Assert.Equal(before?.Board.ToMatrix(), after.Board.ToMatrix());
        }
        else
        {
            AssertSameState(before, after);
        }
    }

    private static void AssertSameState(StoredBoard? expected, StoredBoard? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.Board.ToMatrix(), actual.Board.ToMatrix());
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Generation, actual.Generation);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.CompletedAt, actual.CompletedAt);
        Assert.Equal(expected.CycleStartGeneration, actual.CycleStartGeneration);
        Assert.Equal(expected.Period, actual.Period);
    }

    private static NpgsqlBoardRepository Repository(NpgsqlDataSource source) => new(source, NullLogger<NpgsqlBoardRepository>.Instance);
}