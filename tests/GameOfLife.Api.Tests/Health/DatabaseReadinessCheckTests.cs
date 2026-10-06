using System.Net;

using GameOfLife.Api.Health;
using GameOfLife.Api.Persistence.Migrations;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace GameOfLife.Api.Tests.Health;

[Collection(PostgresFixture.CollectionName)]
public sealed class DatabaseReadinessCheckTests(ApiFactory factory, PostgresFixture postgres) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task MigratedDatabasePassesReadinessAndLiveness()
    {
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task DatabaseMigratedByANewerBuildPassesReadinessAndLiveness()
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(
            "INSERT INTO schema_migrations (version, name) VALUES (9999, 'added_by_a_newer_build')");
        await command.ExecuteNonQueryAsync();
        using var configuredFactory = factory.WithSettings(("ConnectionStrings:GameOfLife", connectionString));
        using var client = configuredFactory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task EmptyDatabaseFailsReadinessButNotLiveness()
    {
        string connectionString = await postgres.CreateDatabaseAsync();

        await AssertNotReadyButLiveAsync(connectionString, "schema_migrations table does not exist");
    }

    [Fact]
    public async Task DatabaseWithoutTheLatestMigrationFailsReadinessButNotLiveness()
    {
        string connectionString = await postgres.CreateDatabaseAsync();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var scripts = EmbeddedMigrationScripts.Load();
        int latestVersion = scripts[^1].Version;
        // With one embedded script this applies none and leaves schema_migrations empty.
        await new MigrationRunner(dataSource, NullLogger<MigrationRunner>.Instance)
            .ApplyAsync(scripts.SkipLast(1).ToList(), CancellationToken.None);

        await AssertNotReadyButLiveAsync(connectionString, $"missing from schema_migrations: {latestVersion}.");
    }

    [Fact]
    public async Task ConnectionStringToAClosedPortFailsReadinessButNotLiveness()
    {
        // Nothing listens on port 1.
        await AssertNotReadyButLiveAsync("Host=127.0.0.1;Port=1;Username=unused;Database=unused", "could not be reached");
    }

    [Fact]
    public async Task ConnectionStringToAMissingDatabaseFailsReadinessButNotLiveness()
    {
        // The real server, but a database that does not exist, so PostgreSQL reports an error other than a missing table.
        var builder = new NpgsqlConnectionStringBuilder(await postgres.CreateDatabaseAsync()) { Database = "no_such_database" };

        await AssertNotReadyButLiveAsync(builder.ConnectionString, "could not be reached or queried");
    }

    // Also runs the check directly: its description goes to the log, not the response.
    private async Task AssertNotReadyButLiveAsync(string connectionString, string expectedDescriptionPart)
    {
        using var configuredFactory = factory.WithSettings(("ConnectionStrings:GameOfLife", connectionString));
        using var client = configuredFactory.CreateClient();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");
        var result = await new DatabaseReadinessCheck(dataSource).CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("application/problem+json", ready.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(expectedDescriptionPart, await ready.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains(expectedDescriptionPart, result.Description);
    }
}