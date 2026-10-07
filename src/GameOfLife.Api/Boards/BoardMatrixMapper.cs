using System.Diagnostics.CodeAnalysis;

using GameOfLife.Core;

namespace GameOfLife.Api.Boards;

public static class BoardMatrixMapper
{
    public static int[][] ToMatrix(Board board)
    {
        var rows = new int[board.Rows][];
        for (int row = 0; row < board.Rows; row++)
        {
            rows[row] = new int[board.Columns];
            for (int column = 0; column < board.Columns; column++)
            {
                rows[row][column] = board[row, column] ? 1 : 0;
            }
        }

        return rows;
    }

    public static Board FromMatrix(int[][]? cells, int rows, int columns)
    {
        if (cells is null || cells.Length != rows || rows < 1 || columns < 1)
        {
            throw new InvalidOperationException("The board matrix has invalid dimensions.");
        }

        if (!TryFromMatrix(cells, rows, columns, out var board, out var errors)
            || board.Columns != columns)
        {
            throw new InvalidOperationException(errors.Count > 0
                ? errors.First().Value[0]
                : "The board matrix has invalid dimensions.");
        }

        return board;
    }

    public static bool TryFromMatrix(
        int[][]? cells, int maxRows, int maxColumns,
        [NotNullWhen(true)] out Board? board, out Dictionary<string, string[]> errors)
    {
        board = null;
        errors = new Dictionary<string, string[]>();

        if (cells is null)
        {
            errors["cells"] = ["The cells field is required; it is missing or null."];
            return false;
        }

        if (cells.Length < 1 || cells.Length > maxRows)
        {
            errors["cells"] = [$"A board must have 1 to {maxRows} rows; this one has {cells.Length}."];
            return false;
        }

        // JSON can contain null rows despite the non-nullable element type.
        for (int row = 0; row < cells.Length; row++)
        {
            if (cells[row] is null)
            {
                errors[$"cells[{row}]"] = ["A row must be an array of cells; this one is null."];
                return false;
            }
        }

        int columns = cells[0].Length;
        if (columns < 1 || columns > maxColumns)
        {
            errors["cells[0]"] = [$"A row must have 1 to {maxColumns} cells; this one has {columns}."];
            return false;
        }

        for (int row = 1; row < cells.Length; row++)
        {
            if (cells[row].Length != columns)
            {
                errors[$"cells[{row}]"] = [$"Every row must have as many cells as the first row, which has {columns}; this one has {cells[row].Length}."];
                return false;
            }
        }

        var values = new bool[checked(cells.Length * columns)];
        for (int row = 0; row < cells.Length; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int value = cells[row][column];
                if (value is not (0 or 1))
                {
                    errors[$"cells[{row}][{column}]"] = [$"A cell must be 0 or 1; this one is {value}."];
                    return false;
                }

                values[(row * columns) + column] = value == 1;
            }
        }

        board = Board.FromCellArray(cells.Length, columns, values);
        return true;
    }
}