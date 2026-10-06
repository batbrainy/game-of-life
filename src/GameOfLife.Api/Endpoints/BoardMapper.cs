using GameOfLife.Core;

namespace GameOfLife.Api.Endpoints;

/// <summary>Converts a <see cref="Board"/> into the <see cref="BoardStateResponse"/> that the board endpoints return.</summary>
public static class BoardMapper
{
    /// <summary>
    /// Builds the response for <paramref name="board"/>, the state of the board stored under <paramref name="id"/>
    /// at <paramref name="generation"/>.
    /// </summary>
    public static BoardStateResponse ToStateResponse(Guid id, int generation, Board board)
    {
        var cells = new int[board.Rows][];
        for (int row = 0; row < board.Rows; row++)
        {
            cells[row] = new int[board.Columns];
            for (int column = 0; column < board.Columns; column++)
            {
                cells[row][column] = board[row, column] ? 1 : 0;
            }
        }

        return new BoardStateResponse(id, generation, board.Rows, board.Columns, cells);
    }
}