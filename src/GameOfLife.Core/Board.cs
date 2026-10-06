namespace GameOfLife.Core;

/// <summary>A grid of live (<see langword="true"/>) and dead cells that cannot change after it is created.</summary>
public sealed class Board : IEquatable<Board>
{
    // The grid row by row: the cell at (row, column) is _cells[row * Columns + column].
    private readonly bool[] _cells;
    private readonly int _hashCode;

    private Board(int rows, int columns, bool[] cells)
    {
        Rows = rows;
        Columns = columns;
        _cells = cells;

        var hash = new HashCode();
        hash.Add(rows);
        hash.Add(columns);
        foreach (bool cell in cells)
        {
            hash.Add(cell);
        }

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

            return _cells[(row * Columns) + column];
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

        var copy = new bool[cells.Length];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                copy[(row * columns) + column] = cells[row, column];
            }
        }

        return new Board(rows, columns, copy);
    }

    /// <summary>Returns a copy of the cells, row by row: the cell at (row, column) is at index row * Columns + column.</summary>
    public bool[] ToCellArray() => _cells.ToArray();

    /// <summary>Creates a board from a copy of <paramref name="cells"/>, laid out as <see cref="ToCellArray"/> returns them.</summary>
    public static Board FromCellArray(int rows, int columns, bool[] cells)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentNullException.ThrowIfNull(cells);

        // Multiplied as longs: as ints, 65,536 x 65,536 wraps around to 0 and would accept an empty array.
        long cellCount = (long)rows * columns;
        if (cells.Length != cellCount)
        {
            throw new ArgumentException($"A {rows}x{columns} board has {cellCount} cells, but {cells.Length} were given.", nameof(cells));
        }

        return new Board(rows, columns, cells.ToArray());
    }

    public bool Equals(Board? other) =>
        other is not null
        && Rows == other.Rows
        && Columns == other.Columns
        && _cells.AsSpan().SequenceEqual(other._cells);

    public override bool Equals(object? obj) => Equals(obj as Board);

    public override int GetHashCode() => _hashCode;
}