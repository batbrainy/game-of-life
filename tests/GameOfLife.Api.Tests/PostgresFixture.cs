using Npgsql;

using Testcontainers.PostgreSql;

namespace GameOfLife.Api.Tests;

// One PostgreSQL container for the whole test run; tests isolate themselves by each creating a database.
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string CollectionName = "PostgreSQL";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Creates an empty database and returns a connection string to it.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
        string name = $"test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            // Identifiers cannot be parameters; the name is generated above, never taken from input.
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        builder.Database = name;
        return builder.ConnectionString;
    }
}

[CollectionDefinition(PostgresFixture.CollectionName)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>;