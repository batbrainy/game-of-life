using System.Runtime.InteropServices;

using Npgsql;

namespace GameOfLife.Api.Persistence.Migrations;

/// <summary>The <c>migrate</c> command: applies the embedded scripts and returns an exit code, without serving requests.</summary>
public static partial class MigrateCommand
{
    public const string Name = "migrate";

    /// <returns>0 when every script is applied, including when none was pending; 1 when the run failed or was cancelled.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrateCommand));
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            LogMissingConnectionString(logger);
            return 1;
        }

        // Ctrl+C sends SIGINT, and docker stop sends SIGTERM. Each one cancels the run instead of ending the process
        // at once, so the open transaction rolls back and the exit code is still returned.
        using var cancellation = new CancellationTokenSource();
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, CancelRun);
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, CancelRun);

        try
        {
            var runner = new MigrationRunner(
                services.GetRequiredService<NpgsqlDataSource>(),
                services.GetRequiredService<ILogger<MigrationRunner>>());
            var applied = await runner.ApplyAsync(EmbeddedMigrationScripts.Load(), cancellation.Token);
            LogUpToDate(logger, applied.Count);
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            LogCancelled(logger);
            return 1;
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
            return 1;
        }

        void CancelRun(PosixSignalContext context)
        {
            context.Cancel = true;
            cancellation.Cancel();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Cannot run migrations: the connection string is missing. Set ConnectionStrings:GameOfLife, for example with the environment variable ConnectionStrings__GameOfLife.")]
    private static partial void LogMissingConnectionString(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The database is up to date; this run applied {AppliedCount} migrations")]
    private static partial void LogUpToDate(ILogger logger, int appliedCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "The migration run was cancelled")]
    private static partial void LogCancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The migration run failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}