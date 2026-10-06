namespace GameOfLife.Core.Tests;

// Text form of a grid for tests: rows separated by '/', '#' for a live cell and '.' for a dead one.
// "#.#/.##" is two rows of three cells.
internal static class Pattern
{
    public static bool[,] Parse(string pattern)
    {
        string[] rows = pattern.Split('/');
        var cells = new bool[rows.Length, rows[0].Length];
        for (int row = 0; row < rows.Length; row++)
        {
            if (rows[row].Length != rows[0].Length)
            {
                throw new ArgumentException($"Row {row} of \"{pattern}\" differs in length from row 0.", nameof(pattern));
            }

            for (int column = 0; column < rows[row].Length; column++)
            {
                cells[row, column] = rows[row][column] switch
                {
                    '#' => true,
                    '.' => false,
                    _ => throw new ArgumentException($"\"{pattern}\" contains '{rows[row][column]}'.", nameof(pattern)),
                };
            }
        }

        return cells;
    }

    // A rows x columns board, dead except for the pattern, whose top-left cell is at (top, left).
    public static Board Place(int rows, int columns, int top, int left, string pattern)
    {
        var shape = Parse(pattern);
        var cells = new bool[rows, columns];
        for (int row = 0; row < shape.GetLength(0); row++)
        {
            for (int column = 0; column < shape.GetLength(1); column++)
            {
                cells[top + row, left + column] = shape[row, column];
            }
        }

        return Board.FromCells(cells);
    }

    public static string Format(Board board)
    {
        var rows = new string[board.Rows];
        for (int row = 0; row < board.Rows; row++)
        {
            var line = new char[board.Columns];
            for (int column = 0; column < board.Columns; column++)
            {
                line[column] = board[row, column] ? '#' : '.';
            }

            rows[row] = new string(line);
        }

        return string.Join('/', rows);
    }
}