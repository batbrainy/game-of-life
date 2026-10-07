using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GameOfLife.Core;

/// <summary>A rectangular 0/1 matrix that cannot change after it is created.</summary>
public sealed class Board
{
    private readonly int[][] _cells;

    private Board(int[][] cells) => _cells = cells;

    public int Rows => _cells.Length;

    public int Columns => _cells[0].Length;

    public int this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
            ArgumentOutOfRangeException.ThrowIfNegative(column);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);

            return _cells[row][column];
        }
    }

    /// <summary>
    /// Returns the next generation: a live cell with 2 or 3 live neighbours stays alive, a dead cell with exactly 3
    /// becomes alive, and every other cell is dead. Positions outside the grid count as dead.
    /// </summary>
    public Board Next()
    {
        var next = new int[Rows][];
        for (int row = 0; row < Rows; row++)
        {
            next[row] = new int[Columns];
            for (int column = 0; column < Columns; column++)
            {
                int liveNeighbours = CountLiveNeighbours(row, column);
                next[row][column] = liveNeighbours == 3 || (_cells[row][column] == 1 && liveNeighbours == 2) ? 1 : 0;
            }
        }

        return new Board(next);
    }

    /// <summary>Creates a board from a deep copy of a nonempty rectangular 0/1 matrix.</summary>
    public static Board FromMatrix(int[][] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Length == 0 || cells[0] is null || cells[0].Length == 0)
        {
            throw new ArgumentException("A board needs at least one row and one column.", nameof(cells));
        }

        int columns = cells[0].Length;
        if ((long)cells.Length * columns > Array.MaxLength)
        {
            throw new ArgumentException("The board exceeds the supported cell count.", nameof(cells));
        }

        foreach (var row in cells)
        {
            if (row is null || row.Length != columns || row.Any(cell => cell is not (0 or 1)))
            {
                throw new ArgumentException("A board must be a rectangular matrix of 0 and 1 cells.", nameof(cells));
            }
        }

        return new Board(CopyMatrix(cells));
    }

    /// <summary>Returns a deep copy of the cells, indexed [row][column].</summary>
    public int[][] ToMatrix() => CopyMatrix(_cells);

    /// <summary>SHA-256 of the dimensions (two big-endian int32s) followed by row-major 0/1 bytes.</summary>
    public string Fingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> dimensions = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(dimensions, Rows);
        BinaryPrimitives.WriteInt32BigEndian(dimensions[4..], Columns);
        hash.AppendData(dimensions);

        var buffer = new byte[Columns];
        foreach (var row in _cells)
        {
            for (int column = 0; column < Columns; column++)
            {
                buffer[column] = (byte)row[column];
            }

            hash.AppendData(buffer);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static int[][] CopyMatrix(int[][] cells) => cells.Select(row => row.ToArray()).ToArray();

    private int CountLiveNeighbours(int row, int column)
    {
        int count = 0;
        for (int neighbourRow = row - 1; neighbourRow <= row + 1; neighbourRow++)
        {
            for (int neighbourColumn = column - 1; neighbourColumn <= column + 1; neighbourColumn++)
            {
                bool isTheCellItself = neighbourRow == row && neighbourColumn == column;
                bool isOutsideTheGrid = neighbourRow < 0 || neighbourRow >= Rows || neighbourColumn < 0 || neighbourColumn >= Columns;
                if (!isTheCellItself && !isOutsideTheGrid)
                {
                    count += _cells[neighbourRow][neighbourColumn];
                }
            }
        }

        return count;
    }
}