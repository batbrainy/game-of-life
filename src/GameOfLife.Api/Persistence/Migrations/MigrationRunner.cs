using System.Diagnostics;

using Npgsql;

namespace GameOfLife.Api.Persistence.Migrations;

/// <summary>Applies numbered SQL scripts to PostgreSQL, each one once, in version order.</summary>
public sealed partial class MigrationRunner(NpgsqlDataSource dataSource, ILogger<MigrationRunner> logger)
{
    // Key of the transaction-level advisory lock every run takes first, so overlapping runs against
    // one database queue instead of interleaving. Any fixed value works: nothing else takes advisory locks.
    private const long AdvisoryLockKey = 7_142_385_901;

    private const string CreateHistoryTableSql = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version    integer     PRIMARY KEY,
            name       text        NOT NULL,
            applied_at timestamptz NOT NULL DEFAULT now()
        )
        """;

    /// <summary>
    /// Runs the scripts whose versions are not yet recorded, in one transaction: either all of them are
    /// applied or none is.
    /// </summary>
    /// <returns>The versions applied by this call, in ascending order.</returns>
    public async Task<IReadOnlyList<int>> ApplyAsync(IReadOnlyList<MigrationScript> scripts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        EnsureVersionsAreValid(scripts);
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, "SELECT pg_advisory_xact_lock($1)", [AdvisoryLockKey], cancellationToken);
        await ExecuteAsync(connection, transaction, CreateHistoryTableSql, [], cancellationToken);
        var recordedVersions = await ReadRecordedVersionsAsync(connection, transaction, cancellationToken);

        var applied = new List<(MigrationScript Script, long ElapsedMilliseconds)>();
        foreach (var script in scripts.Where(script => !recordedVersions.Contains(script.Version)).OrderBy(script => script.Version))
        {
            var stopwatch = Stopwatch.StartNew();
            await ExecuteAsync(connection, transaction, script.Sql, [], cancellationToken);
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO schema_migrations (version, name) VALUES ($1, $2)",
                [script.Version, script.Name],
                cancellationToken);
            applied.Add((script, stopwatch.ElapsedMilliseconds));
        }

        await transaction.CommitAsync(cancellationToken);

        // Logged after the commit, so a script is never reported as applied when its run rolled back.
        foreach (var (script, elapsedMilliseconds) in applied)
        {
            LogApplied(logger, script.Version, script.Name, elapsedMilliseconds);
        }

        return applied.ConvertAll(entry => entry.Script.Version);
    }

    private static void EnsureVersionsAreValid(IReadOnlyList<MigrationScript> scripts)
    {
        var versions = new HashSet<int>();
        foreach (var script in scripts)
        {
            if (script.Version < 1)
            {
                throw new ArgumentException($"Migration '{script.Name}' has version {script.Version}; versions start at 1.", nameof(scripts));
            }

            if (!versions.Add(script.Version))
            {
                throw new ArgumentException($"More than one migration has version {script.Version}.", nameof(scripts));
            }
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        object[] parameterValues,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in parameterValues)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<int>> ReadRecordedVersionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT version FROM schema_migrations", connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var versions = new HashSet<int>();
        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied migration {Version} ({Name}) in {ElapsedMilliseconds} ms")]
    private static partial void LogApplied(ILogger logger, int version, string name, long elapsedMilliseconds);
}