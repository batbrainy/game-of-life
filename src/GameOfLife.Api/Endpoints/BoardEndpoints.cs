using GameOfLife.Api.Configuration;
using GameOfLife.Api.Persistence;
using GameOfLife.Core;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace GameOfLife.Api.Endpoints;

/// <summary>The board endpoints, under <c>/api/v1/boards</c>.</summary>
public static partial class BoardEndpoints
{
    public static void MapBoardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var boards = endpoints.MapGroup("/api/v1/boards");
        boards.MapPost("/", UploadAsync);
        // No {id:guid} constraint: an id that is not a GUID then fails binding with 400, instead of matching no route with 404.
        boards.MapGet("/{id}", FetchAsync);
        boards.MapGet("/{id}/next", FetchNextGenerationAsync);
    }

    private static async Task<Results<Created<UploadBoardResponse>, ValidationProblem>> UploadAsync(
        UploadBoardRequest request,
        IBoardRepository repository,
        IOptions<GameOfLifeOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!BoardRequestValidator.TryValidate(request.Cells, options.Value, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        int rows = request.Cells.Length;
        int columns = request.Cells[0].Length;
        var cells = new bool[rows, columns];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                cells[row, column] = request.Cells[row][column] == 1;
            }
        }

        var id = Guid.NewGuid();
        await repository.AddAsync(id, Board.FromCells(cells), cancellationToken);

        LogStored(loggerFactory.CreateLogger(typeof(BoardEndpoints)), id, rows, columns);
        return TypedResults.Created($"/api/v1/boards/{id}", new UploadBoardResponse(id));
    }

    private static async Task<Results<Ok<BoardStateResponse>, ProblemHttpResult>> FetchAsync(
        Guid id,
        IBoardRepository repository,
        CancellationToken cancellationToken)
    {
        var board = await repository.FindAsync(id, cancellationToken);
        if (board is null)
        {
            return BoardNotFound(id);
        }

        return TypedResults.Ok(BoardMapper.ToStateResponse(id, generation: 0, board));
    }

    private static async Task<Results<Ok<BoardStateResponse>, ProblemHttpResult>> FetchNextGenerationAsync(
        Guid id,
        IBoardRepository repository,
        CancellationToken cancellationToken)
    {
        var board = await repository.FindAsync(id, cancellationToken);
        if (board is null)
        {
            return BoardNotFound(id);
        }

        return TypedResults.Ok(BoardMapper.ToStateResponse(id, generation: 1, board.Next()));
    }

    private static ProblemHttpResult BoardNotFound(Guid id) => TypedResults.Problem(
        detail: $"No board is stored under the id {id}.",
        statusCode: StatusCodes.Status404NotFound,
        title: "Board not found");

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored board {BoardId} of {Rows} x {Columns} cells")]
    private static partial void LogStored(ILogger logger, Guid boardId, int rows, int columns);
}