using GameOfLife.Api.Persistence.Migrations;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace GameOfLife.Api.Tests.Persistence;

[Collection(PostgresFixture.CollectionName)]
public sealed class MigrationRunnerTests(PostgresFixture postgres)
{
    private static readonly MigrationScript CreateWidgets = new(1, "create_widgets", "CREATE TABLE widgets (id integer PRIMARY KEY)");

    // Fails unless version 1 has already run, so it also checks the order of application.
    private static readonly MigrationScript AddWidgetName = new(2, "add_widget_name", "ALTER TABLE widgets ADD COLUMN name text");

    private static readonly MigrationScript CreateGadgets = new(3, "create_gadgets", "CREATE TABLE gadgets (id integer PRIMARY KEY)");

    [Fact]
    public async Task AppliesScriptsPassedOutOfOrderInVersionOrderAndRecordsThem()
    {
        await using var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());

        var applied = await CreateRunner(dataSource).ApplyAsync([AddWidgetName, CreateWidgets], CancellationToken.None);

        Assert.Equal<int>([1, 2], applied);
        Assert.Equal(new[] { (1, "create_widgets"), (2, "add_widget_name") }, await ReadRecordedAsync(dataSource));
    }

    [Fact]
    public async Task SecondRunAppliesNothing()
    {
        await using var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        var runner = CreateRunner(dataSource);
        await runner.ApplyAsync([CreateWidgets, AddWidgetName], CancellationToken.None);

        var applied = await runner.ApplyAsync([CreateWidgets, AddWidgetName], CancellationToken.None);

        Assert.Empty(applied);
        Assert.Equal(new[] { (1, "create_widgets"), (2, "add_widget_name") }, await ReadRecordedAsync(dataSource));
    }

    [Fact]
    public async Task ScriptAddedLaterIsTheOnlyOneApplied()
    {
        await using var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        var runner = CreateRunner(dataSource);
        await runner.ApplyAsync([CreateWidgets, AddWidgetName], CancellationToken.None);

        var applied = await runner.ApplyAsync([CreateWidgets, AddWidgetName, CreateGadgets], CancellationToken.None);

        Assert.Equal<int>([3], applied);
        Assert.Equal(
            new[] { (1, "create_widgets"), (2, "add_widget_name"), (3, "create_gadgets") },
            await ReadRecordedAsync(dataSource));
    }

    [Fact]
    public async Task FailingSecondScriptLeavesNoTables()
    {
        await using var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        var failing = new MigrationScript(2, "duplicate_column", "CREATE TABLE gadgets (id integer, id integer)");

        await Assert.ThrowsAsync<PostgresException>(
            () => CreateRunner(dataSource).ApplyAsync([CreateWidgets, failing], CancellationToken.None));

        Assert.Empty(await ReadTableNamesAsync(dataSource));
    }

    [Fact]
    public async Task ConcurrentRunsBothFinishAndRecordEachVersionOnce()
    {
        await using var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        // The pause holds the first run's transaction open while the second run starts.
        var slowCreateWidgets = CreateWidgets with { Sql = CreateWidgets.Sql + "; SELECT pg_sleep(0.5)" };
        MigrationScript[] scripts = [slowCreateWidgets, AddWidgetName];

        var results = await Task.WhenAll(
            CreateRunner(dataSource).ApplyAsync(scripts, CancellationToken.None),
            CreateRunner(dataSource).ApplyAsync(scripts, CancellationToken.None));

        Assert.Contains(results, applied => applied.SequenceEqual([1, 2]));
        Assert.Contains(results, applied => applied.Count == 0);
        Assert.Equal(new[] { (1, "create_widgets"), (2, "add_widget_name") }, await ReadRecordedAsync(dataSource));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(0, 1)]
    [InlineData(1, -1)]
    public async Task DuplicateOrNonPositiveVersionsAreRejectedBeforeConnecting(int firstVersion, int secondVersion)
    {
        // Nothing listens on port 1, so reaching the database would fail with an NpgsqlException instead.
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=unused;Database=unused");
        MigrationScript[] scripts = [new(firstVersion, "first", "SELECT 1"), new(secondVersion, "second", "SELECT 1")];

        await Assert.ThrowsAsync<ArgumentException>(() => CreateRunner(dataSource).ApplyAsync(scripts, CancellationToken.None));
    }

    [Fact]
    public async Task AlreadyCancelledTokenThrowsAndLeavesTheDatabaseEmpty()
    {
        await using var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateRunner(dataSource).ApplyAsync([CreateWidgets], cancellation.Token));

        Assert.Empty(await ReadTableNamesAsync(dataSource));
    }

    private static MigrationRunner CreateRunner(NpgsqlDataSource dataSource) =>
        new(dataSource, NullLogger<MigrationRunner>.Instance);

    private static async Task<List<(int Version, string Name)>> ReadRecordedAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("SELECT version, name FROM schema_migrations ORDER BY version");
        await using var reader = await command.ExecuteReaderAsync();

        var recorded = new List<(int Version, string Name)>();
        while (await reader.ReadAsync())
        {
            recorded.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return recorded;
    }

    private static async Task<List<string>> ReadTableNamesAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT tablename FROM pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema')");
        await using var reader = await command.ExecuteReaderAsync();

        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}