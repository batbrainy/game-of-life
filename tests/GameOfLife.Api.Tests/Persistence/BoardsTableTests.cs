using GameOfLife.Api.Persistence;
using GameOfLife.Api.Persistence.Migrations;
using GameOfLife.Core;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using NpgsqlTypes;

namespace GameOfLife.Api.Tests.Persistence;

[Collection(PostgresFixture.CollectionName)]
public sealed class BoardsTableTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(2, 3)]
    [InlineData(1, 4)]
    [InlineData(4, 1)]
    public async Task UpgradePreservesLegacyCellsIdsAndTimestamps(int rows, int columns)
    {
        await using var source = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        var runner = new MigrationRunner(source, NullLogger<MigrationRunner>.Instance);
        var scripts = EmbeddedMigrationScripts.Load();
        await runner.ApplyAsync(scripts.Take(1).ToList(), CancellationToken.None);
        var id = Guid.NewGuid();
        var created = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        bool[] cells = Enumerable.Range(0, rows * columns).Select(i => i % 3 == 1).ToArray();
        await using (var insert = source.CreateCommand("INSERT INTO boards (id, row_count, column_count, cells, created_at) VALUES ($1,$2,$3,$4,$5)"))
        {
            insert.Parameters.AddWithValue(id);
            insert.Parameters.AddWithValue(rows);
            insert.Parameters.AddWithValue(columns);
            insert.Parameters.AddWithValue(cells);
            insert.Parameters.AddWithValue(created);
            await insert.ExecuteNonQueryAsync();
        }

        await runner.ApplyAsync(scripts, CancellationToken.None);
        var state = await new NpgsqlBoardRepository(source, NullLogger<NpgsqlBoardRepository>.Instance).FindAsync(id, CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(Enumerable.Range(0, rows).Select(row => cells.Skip(row * columns).Take(columns).Select(cell => cell ? 1 : 0).ToArray()).ToArray(), state.Board.ToMatrix());
        Assert.Equal(created, state.CreatedAt);
        Assert.Equal(created, state.UpdatedAt);
        Assert.Equal(0, state.Generation);
        Assert.Equal(BoardStatus.Active, state.Status);
        Assert.Null(state.CompletedAt);
        Assert.Empty(await runner.ApplyAsync(scripts, CancellationToken.None));
    }

    [Theory]
    [InlineData("UPDATE boards SET status = 'Unknown'")]
    [InlineData("UPDATE boards SET status = 'Stable'")]
    [InlineData("UPDATE boards SET generation = -1")]
    [InlineData("UPDATE boards SET cycle_length = 2")]
    [InlineData("UPDATE boards SET cells = '{}'::jsonb")]
    [InlineData("UPDATE boards SET cells = '[]'::jsonb")]
    public async Task InvalidStateCannotBeStored(string sql)
    {
        await using var source = NpgsqlDataSource.Create(await postgres.CreateMigratedDatabaseAsync());
        var repository = new NpgsqlBoardRepository(source, NullLogger<NpgsqlBoardRepository>.Instance);
        await repository.AddAsync(Guid.NewGuid(), Board.FromMatrix([[1]]), CancellationToken.None);
        await using var command = source.CreateCommand(sql);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData("[[2]]")]
    [InlineData("[[]]")]
    [InlineData("[null]")]
    public async Task CorruptedInnerMatrixIsRejectedWhenLoaded(string json)
    {
        await using var source = NpgsqlDataSource.Create(await postgres.CreateMigratedDatabaseAsync());
        var id = Guid.NewGuid();
        await using var insert = source.CreateCommand("INSERT INTO boards(id,row_count,column_count,cells) VALUES($1,1,1,$2)");
        insert.Parameters.AddWithValue(id);
        insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, json);
        await insert.ExecuteNonQueryAsync();
        var repository = new NpgsqlBoardRepository(source, NullLogger<NpgsqlBoardRepository>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.FindAsync(id, CancellationToken.None));
    }
}