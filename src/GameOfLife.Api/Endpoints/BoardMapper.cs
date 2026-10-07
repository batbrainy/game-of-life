using GameOfLife.Api.Boards;
using GameOfLife.Api.Persistence;

namespace GameOfLife.Api.Endpoints;

public static class BoardMapper
{
    public static BoardStateResponse ToStateResponse(StoredBoard state) => new(
        state.Id, state.Generation, state.Board.Rows, state.Board.Columns, BoardMatrixMapper.ToMatrix(state.Board),
        state.Status.ToString(), state.CycleStartGeneration, state.Period);
}