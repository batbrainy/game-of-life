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

        var values = new bool[checked(rows * columns)];
        for (int row = 0; row < rows; row++)
        {
            if (cells[row] is null || cells[row].Length != columns)
            {
                throw new InvalidOperationException("The board matrix is not rectangular.");
            }

            for (int column = 0; column < columns; column++)
            {
                int value = cells[row][column];
                if (value is not (0 or 1))
                {
                    throw new InvalidOperationException("The board matrix contains an invalid cell.");
                }

                values[(row * columns) + column] = value == 1;
            }
        }

        return Board.FromCellArray(rows, columns, values);
    }
}