using GameOfLife.Core;

using Npgsql;

namespace GameOfLife.Api.Persistence;

/// <summary>Keeps boards in the PostgreSQL <c>boards</c> table. It has an insert and a select, and no update or delete.</summary>
public sealed class NpgsqlBoardRepository(NpgsqlDataSource dataSource) : IBoardRepository
{
    public async Task AddAsync(Guid id, Board board, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(board);

        await using var command = dataSource.CreateCommand(
            "INSERT INTO boards (id, row_count, column_count, cells) VALUES ($1, $2, $3, $4)");
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        command.Parameters.Add(new NpgsqlParameter { Value = board.Rows });
        command.Parameters.Add(new NpgsqlParameter { Value = board.Columns });
        // Npgsql sends a bool[] as a PostgreSQL boolean[], keeping the row-by-row order.
        command.Parameters.Add(new NpgsqlParameter { Value = board.ToCellArray() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Board?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var command = dataSource.CreateCommand("SELECT row_count, column_count, cells FROM boards WHERE id = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return Board.FromCellArray(reader.GetInt32(0), reader.GetInt32(1), reader.GetFieldValue<bool[]>(2));
    }
}