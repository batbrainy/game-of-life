using GameOfLife.Api.Persistence.Migrations;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace GameOfLife.Api.Health;

/// <summary>Healthy when the database answers a query and <c>schema_migrations</c> records every embedded migration.</summary>
public sealed class DatabaseReadinessCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var recordedVersions = new HashSet<int>();
        try
        {
            await using var command = dataSource.CreateCommand("SELECT version FROM schema_migrations");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                recordedVersions.Add(reader.GetInt32(0));
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return HealthCheckResult.Unhealthy("The schema_migrations table does not exist; the migrate command has not run.", exception);
        }
        catch (NpgsqlException exception)
        {
            return HealthCheckResult.Unhealthy("The database could not be reached or queried.", exception);
        }

        // Only embedded versions are looked for, so a database migrated further by a newer build still counts as ready.
        var missingVersions = new List<int>();
        foreach (var script in EmbeddedMigrationScripts.Load())
        {
            if (!recordedVersions.Contains(script.Version))
            {
                missingVersions.Add(script.Version);
            }
        }

        if (missingVersions.Count > 0)
        {
            return HealthCheckResult.Unhealthy(
                $"Migration versions missing from schema_migrations: {string.Join(", ", missingVersions)}.");
        }

        return HealthCheckResult.Healthy();
    }
}