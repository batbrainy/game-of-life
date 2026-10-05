namespace GameOfLife.Core;

/// <summary>A grid of live (<see langword="true"/>) and dead cells that cannot change after it is created.</summary>
public sealed class Board : IEquatable<Board>
{
    // One bit per cell, row-major: cell i = row * Columns + column is bit i % 8 (least significant
    // first) of byte i / 8. The unused bits of the last byte stay 0, so equal boards have equal bytes.
    private readonly byte[] _cells;
    private readonly int _hashCode;

    private Board(int rows, int columns, byte[] cells)
    {
        Rows = rows;
        Columns = columns;
        _cells = cells;

        var hash = new HashCode();
        hash.Add(rows);
        hash.Add(columns);
        hash.AddBytes(cells);
        _hashCode = hash.ToHashCode();
    }

    public int Rows { get; }

    public int Columns { get; }

    public bool this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
            ArgumentOutOfRangeException.ThrowIfNegative(column);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);

            int index = (row * Columns) + column;
            return (_cells[index / 8] & (1 << (index % 8))) != 0;
        }
    }

    /// <summary>Creates a board from a copy of <paramref name="cells"/>, indexed [row, column].</summary>
    public static Board FromCells(bool[,] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        int rows = cells.GetLength(0);
        int columns = cells.GetLength(1);
        if (rows == 0 || columns == 0)
        {
            throw new ArgumentException("A board needs at least one row and one column.", nameof(cells));
        }

        // Cell indexes are ints, and a bool[,] can hold more than int.MaxValue elements.
        if (cells.LongLength > int.MaxValue)
        {
            throw new ArgumentException($"A board can hold at most {int.MaxValue} cells.", nameof(cells));
        }

        var packed = new byte[(cells.LongLength + 7) / 8];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                if (cells[row, column])
                {
                    int index = (row * columns) + column;
                    packed[index / 8] |= (byte)(1 << (index % 8));
                }
            }
        }

        return new Board(rows, columns, packed);
    }

    public bool Equals(Board? other) =>
        other is not null
        && Rows == other.Rows
        && Columns == other.Columns
        && _cells.AsSpan().SequenceEqual(other._cells);

    public override bool Equals(object? obj) => Equals(obj as Board);

    public override int GetHashCode() => _hashCode;
}