using System.Net;

using Npgsql;

namespace GameOfLife.Api.Tests.Persistence;

[Collection(PostgresFixture.CollectionName)]
public sealed class MigrateCommandTests(PostgresFixture postgres)
{
    [Fact]
    public async Task EmptyDatabaseGetsTheBoardsTableAndTheExitCodeIsZero()
    {
        string connectionString = await postgres.CreateDatabaseAsync();

        int exitCode = await RunProgramAsync("migrate", $"--ConnectionStrings:GameOfLife={connectionString}");

        Assert.Equal(0, exitCode);
        Assert.Contains("boards", await ReadTableNamesAsync(connectionString));
    }

    [Fact]
    public async Task SecondRunAppliesNothingAndTheExitCodeIsZero()
    {
        string connectionString = await postgres.CreateDatabaseAsync();
        await RunProgramAsync("migrate", $"--ConnectionStrings:GameOfLife={connectionString}");
        var recordedByFirstRun = await ReadRecordedAsync(connectionString);

        int exitCode = await RunProgramAsync("migrate", $"--ConnectionStrings:GameOfLife={connectionString}");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, Assert.Single(recordedByFirstRun).Version);
        Assert.Equal(recordedByFirstRun, await ReadRecordedAsync(connectionString));
    }

    [Fact]
    public async Task MissingOrEmptyConnectionStringGivesExitCodeOne()
    {
        Assert.Equal(1, await RunProgramAsync("migrate"));
        Assert.Equal(1, await RunProgramAsync("migrate", "--ConnectionStrings:GameOfLife="));
    }

    [Fact]
    public async Task ConnectionStringToAClosedPortGivesExitCodeOne()
    {
        // Nothing listens on port 1.
        int exitCode = await RunProgramAsync(
            "migrate", "--ConnectionStrings:GameOfLife=Host=127.0.0.1;Port=1;Username=unused;Database=unused");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ApiStartedWithoutTheCommandRunsNoMigrations()
    {
        string connectionString = await postgres.CreateDatabaseAsync();
        using var api = new ApiFactory(postgres);
        using var configuredApi = api.WithSettings(("ConnectionStrings:GameOfLife", connectionString));
        using var client = configuredApi.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ReadTableNamesAsync(connectionString));
    }

    // Calls the Main method generated from Program.cs, as running the built application with these arguments would.
    private static async Task<int> RunProgramAsync(params string[] args)
    {
        var main = typeof(Program).Assembly.EntryPoint
            ?? throw new InvalidOperationException("The API assembly has no entry point.");

        // Main blocks until the command finishes; Task.Run keeps that wait off xUnit's limited test threads.
        object? exitCode = await Task.Run(() => main.Invoke(null, [args]));

        return Assert.IsType<int>(exitCode);
    }

    private static async Task<List<(int Version, DateTime AppliedAt)>> ReadRecordedAsync(string connectionString)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand("SELECT version, applied_at FROM schema_migrations ORDER BY version");
        await using var reader = await command.ExecuteReaderAsync();

        var recorded = new List<(int Version, DateTime AppliedAt)>();
        while (await reader.ReadAsync())
        {
            recorded.Add((reader.GetInt32(0), reader.GetDateTime(1)));
        }

        return recorded;
    }

    private static async Task<List<string>> ReadTableNamesAsync(string connectionString)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
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