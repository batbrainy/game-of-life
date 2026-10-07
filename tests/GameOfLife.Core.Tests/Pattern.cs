namespace GameOfLife.Core.Tests;

// Text form of a grid for tests: rows separated by '/', '#' for a live cell and '.' for a dead one.
// "#.#/.##" is two rows of three cells.
internal static class Pattern
{
    public static int[][] Parse(string pattern)
    {
        string[] rows = pattern.Split('/');
        var cells = new int[rows.Length][];
        for (int row = 0; row < rows.Length; row++)
        {
            if (rows[row].Length != rows[0].Length)
            {
                throw new ArgumentException($"Row {row} of \"{pattern}\" differs in length from row 0.", nameof(pattern));
            }

            cells[row] = new int[rows[row].Length];
            for (int column = 0; column < rows[row].Length; column++)
            {
                cells[row][column] = rows[row][column] switch
                {
                    '#' => 1,
                    '.' => 0,
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
        var cells = Enumerable.Range(0, rows).Select(_ => new int[columns]).ToArray();
        for (int row = 0; row < shape.Length; row++)
        {
            for (int column = 0; column < shape[row].Length; column++)
            {
                cells[top + row][left + column] = shape[row][column];
            }
        }

        return Board.FromMatrix(cells);
    }

    public static string Format(Board board)
    {
        var rows = new string[board.Rows];
        for (int row = 0; row < board.Rows; row++)
        {
            var line = new char[board.Columns];
            for (int column = 0; column < board.Columns; column++)
            {
                line[column] = board[row, column] == 1 ? '#' : '.';
            }

            rows[row] = new string(line);
        }

        return string.Join('/', rows);
    }
}