using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using GameOfLife.Core;

using Npgsql;

using NpgsqlTypes;

namespace GameOfLife.Api.Persistence;

public sealed partial class NpgsqlBoardRepository(NpgsqlDataSource dataSource, ILogger<NpgsqlBoardRepository> logger) : IBoardRepository
{
    private const string Columns = "id, row_count, column_count, cells, generation, status, created_at, updated_at, completed_at, cycle_start_generation, cycle_length";

    public async Task AddAsync(Guid id, Board board, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        await using var command = dataSource.CreateCommand(
            "INSERT INTO boards (id, row_count, column_count, cells) VALUES ($1, $2, $3, $4)");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(board.Rows);
        command.Parameters.AddWithValue(board.Columns);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(BoardJson.ToRows(board)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredBoard?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await FindAsync(connection, id, cancellationToken);
    }

    public async Task<IBoardMutationSession> LockAsync(Guid id, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        NpgsqlConnection? connection = null;
        bool acquisitionUncertain = false;
        try
        {
            connection = await dataSource.OpenConnectionAsync(deadline.Token);
            long key = AdvisoryKey(id);
            while (true)
            {
                await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection);
                command.Parameters.AddWithValue(key);
                // Cancellation can arrive after PostgreSQL acquires the lock but before its answer reaches us.
                acquisitionUncertain = true;
                bool acquired = (bool)(await command.ExecuteScalarAsync(deadline.Token)
                    ?? throw new InvalidOperationException("The lock query returned no result."));
                acquisitionUncertain = false;
                if (acquired)
                {
                    var session = new MutationSession(connection, id, key, logger);
                    connection = null; // Ownership transfers to the session, including release and disposal.
                    return session;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new BoardLockTimeoutException();
        }
        finally
        {
            if (connection is not null)
            {
                if (acquisitionUncertain)
                {
                    NpgsqlConnection.ClearPool(connection);
                }

                await connection.DisposeAsync();
            }
        }
    }

    // Canonical GUID text avoids platform byte-order differences. Negative keys cannot collide with the
    // migration runner's positive key. A hash collision only serializes two unrelated boards.
    public static long AdvisoryKey(Guid id) =>
        BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"game-of-life:board:{id:D}"))) | long.MinValue;

    private static async Task<StoredBoard?> FindAsync(NpgsqlConnection connection, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT {Columns} FROM boards WHERE id = $1", connection);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static StoredBoard Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        BoardJson.Deserialize(reader.GetString(3), reader.GetInt32(1), reader.GetInt32(2)),
        reader.GetInt64(4),
        Enum.Parse<BoardStatus>(reader.GetString(5)),
        reader.GetDateTime(6),
        reader.GetDateTime(7),
        reader.IsDBNull(8) ? null : reader.GetDateTime(8),
        reader.IsDBNull(9) ? null : reader.GetInt64(9),
        reader.IsDBNull(10) ? null : reader.GetInt32(10));

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not release the advisory lock for board {BoardId}; discarding pooled sessions")]
    private static partial void LogUnlockFailed(ILogger logger, Guid boardId, Exception exception);

    private sealed class MutationSession(NpgsqlConnection connection, Guid id, long key, ILogger logger) : IBoardMutationSession
    {
        private bool _disposed;

        public Task<StoredBoard?> FindAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return NpgsqlBoardRepository.FindAsync(connection, id, cancellationToken);
        }

        public async Task<StoredBoard> SaveAsync(
            Board board, long generation, BoardStatus status, long? cycleStartGeneration, int? period,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await using var command = new NpgsqlCommand($"""
                UPDATE boards SET cells = $2, generation = $3, status = $4,
                    updated_at = statement_timestamp(),
                    completed_at = CASE WHEN $4 = 'Active' THEN NULL ELSE statement_timestamp() END,
                    cycle_start_generation = $5, cycle_length = $6
                WHERE id = $1 AND row_count = $7 AND column_count = $8
                    AND status = 'Active' AND generation < $3
                RETURNING {Columns}
                """, connection);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(BoardJson.ToRows(board)));
            command.Parameters.AddWithValue(generation);
            command.Parameters.AddWithValue(status.ToString());
            command.Parameters.AddWithValue(NpgsqlDbType.Bigint, (object?)cycleStartGeneration ?? DBNull.Value);
            command.Parameters.AddWithValue(NpgsqlDbType.Integer, (object?)period ?? DBNull.Value);
            command.Parameters.AddWithValue(board.Rows);
            command.Parameters.AddWithValue(board.Columns);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("The locked board could not be advanced.");
            }

            return Read(reader);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock($1)", connection) { CommandTimeout = 5 };
                command.Parameters.AddWithValue(key);
                if (await command.ExecuteScalarAsync(cleanup.Token) is not true)
                {
                    throw new InvalidOperationException("The session no longer owned its advisory lock.");
                }
            }
            catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException or InvalidOperationException)
            {
                // ClearPool marks checked-out connections for destruction when returned. This conservative fallback
                // prevents an uncertain lock from surviving in the pool and preserves the original operation error.
                NpgsqlConnection.ClearPool(connection);
                LogUnlockFailed(logger, id, exception);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}