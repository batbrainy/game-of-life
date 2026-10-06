using GameOfLife.Core;

namespace GameOfLife.Api.Endpoints;

/// <summary>Converts a <see cref="Board"/> or a <see cref="FinalState"/> into the response that the board endpoints return.</summary>
public static class BoardMapper
{
    /// <summary>
    /// Builds the response for <paramref name="board"/>, the state of the board stored under <paramref name="id"/>
    /// at <paramref name="generation"/>.
    /// </summary>
    public static BoardStateResponse ToStateResponse(Guid id, int generation, Board board)
    {
        return new BoardStateResponse(id, generation, board.Rows, board.Columns, ToCellRows(board));
    }

    /// <summary>Builds the response for <paramref name="finalState"/>, the final state of the board stored under <paramref name="id"/>.</summary>
    public static FinalStateResponse ToFinalStateResponse(Guid id, FinalState finalState)
    {
        var board = finalState.Board;
        return new FinalStateResponse(id, finalState.Generation, finalState.Period, board.Rows, board.Columns, ToCellRows(board));
    }

    private static int[][] ToCellRows(Board board)
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

        return cells;
    }
}