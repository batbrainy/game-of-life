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
        boards.MapGet("/{id}/generations/{n}", FetchGenerationAsync);
        boards.MapGet("/{id}/final", FindFinalStateAsync);
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

    private static async Task<Results<Ok<BoardStateResponse>, ValidationProblem, ProblemHttpResult>> FetchGenerationAsync(
        Guid id,
        int n,
        IBoardRepository repository,
        IOptions<GameOfLifeOptions> options,
        CancellationToken cancellationToken)
    {
        int maxGenerationsAhead = options.Value.MaxGenerationsAhead;
        if (n < 0 || n > maxGenerationsAhead)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["n"] = [$"The generation must be 0 to {maxGenerationsAhead}; this request asks for {n}."],
            });
        }

        var board = await repository.FindAsync(id, cancellationToken);
        if (board is null)
        {
            return BoardNotFound(id);
        }

        var advanced = Simulation.Advance(board, n, cancellationToken);
        return TypedResults.Ok(BoardMapper.ToStateResponse(id, generation: n, advanced));
    }

    private static async Task<Results<Ok<FinalStateResponse>, ProblemHttpResult>> FindFinalStateAsync(
        Guid id,
        IBoardRepository repository,
        IOptions<GameOfLifeOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var board = await repository.FindAsync(id, cancellationToken);
        if (board is null)
        {
            return BoardNotFound(id);
        }

        int maxGenerations = options.Value.MaxFinalStateGenerations;
        var finalState = Simulation.FindFinalState(board, maxGenerations, cancellationToken);
        if (finalState is null)
        {
            LogNoFinalState(loggerFactory.CreateLogger(typeof(BoardEndpoints)), id, maxGenerations);
            return TypedResults.Problem(
                detail: $"The board did not repeat an earlier generation within the limit of {maxGenerations} generations.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Board did not reach a final state",
                extensions: new Dictionary<string, object?> { ["maxGenerations"] = maxGenerations });
        }

        return TypedResults.Ok(BoardMapper.ToFinalStateResponse(id, finalState));
    }

    private static ProblemHttpResult BoardNotFound(Guid id) => TypedResults.Problem(
        detail: $"No board is stored under the id {id}.",
        statusCode: StatusCodes.Status404NotFound,
        title: "Board not found");

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored board {BoardId} of {Rows} x {Columns} cells")]
    private static partial void LogStored(ILogger logger, Guid boardId, int rows, int columns);

    [LoggerMessage(Level = LogLevel.Information, Message = "Board {BoardId} did not repeat an earlier generation within {MaxGenerations} generations")]
    private static partial void LogNoFinalState(ILogger logger, Guid boardId, int maxGenerations);
}