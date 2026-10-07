using System.Text.Json;

using GameOfLife.Core;

namespace GameOfLife.Api.Persistence;

public static class BoardJson
{
    public static int[][] ToRows(Board board)
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

    public static Board Deserialize(string json, int rows, int columns)
    {
        int[][]? cells = JsonSerializer.Deserialize<int[][]>(json);
        if (cells is null || cells.Length != rows || rows < 1 || columns < 1)
        {
            throw new InvalidOperationException("The stored board has invalid dimensions.");
        }

        var values = new bool[checked(rows * columns)];
        for (int row = 0; row < rows; row++)
        {
            if (cells[row] is null || cells[row].Length != columns)
            {
                throw new InvalidOperationException("The stored board is not rectangular.");
            }

            for (int column = 0; column < columns; column++)
            {
                int value = cells[row][column];
                if (value is not (0 or 1))
                {
                    throw new InvalidOperationException("The stored board contains an invalid cell.");
                }

                values[(row * columns) + column] = value == 1;
            }
        }

        return Board.FromCellArray(rows, columns, values);
    }
}