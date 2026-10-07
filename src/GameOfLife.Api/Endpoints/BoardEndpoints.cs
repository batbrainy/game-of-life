using GameOfLife.Api.Boards;
using GameOfLife.Api.Configuration;
using GameOfLife.Api.Persistence;
using GameOfLife.Core;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace GameOfLife.Api.Endpoints;

public static partial class BoardEndpoints
{
    public static void MapBoardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var boards = endpoints.MapGroup("/api/v1/boards");

        boards.MapPost("/", UploadAsync)
            .WithName("UploadBoard").WithSummary("Upload a board")
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        boards.MapGet("/{id}", FetchAsync)
            .WithName("GetBoard").WithSummary("Get a stored board")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        boards.MapPost("/{id}/next", NextAsync)
            .WithName("GetNextGeneration").WithSummary("Advance and persist the next generation")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(SimulationConcurrencyLimit.PolicyName);

        boards.MapGet("/{id}/generations/{n}", ProjectAsync)
            .WithName("GetGeneration").WithSummary("Project n generations from the current snapshot without saving")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(SimulationConcurrencyLimit.PolicyName);

        boards.MapPost("/{id}/final", FinalAsync)
            .WithName("GetFinalState").WithSummary("Find and persist a stable state or cycle")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(SimulationConcurrencyLimit.PolicyName);
    }

    private static async Task<Results<Created<UploadBoardResponse>, ValidationProblem>> UploadAsync(
        UploadBoardRequest request, IBoardRepository repository, IOptions<GameOfLifeOptions> options,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
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
        Guid id, IBoardRepository repository, CancellationToken cancellationToken)
    {
        var state = await repository.FindAsync(id, cancellationToken);
        return state is null ? BoardNotFound(id) : TypedResults.Ok(BoardMapper.ToStateResponse(state));
    }

    private static Task<Results<Ok<BoardStateResponse>, ProblemHttpResult>> NextAsync(
        Guid id, BoardService service, IOptions<GameOfLifeOptions> options, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) => MutateAsync(id, false, service, options, loggerFactory, cancellationToken);

    private static Task<Results<Ok<BoardStateResponse>, ProblemHttpResult>> FinalAsync(
        Guid id, BoardService service, IOptions<GameOfLifeOptions> options, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) => MutateAsync(id, true, service, options, loggerFactory, cancellationToken);

    private static async Task<Results<Ok<BoardStateResponse>, ProblemHttpResult>> MutateAsync(
        Guid id, bool toFinalState, BoardService service, IOptions<GameOfLifeOptions> options,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var result = await service.AdvanceAsync(id, toFinalState, cancellationToken);
        switch (result.Error)
        {
            case MutationError.NotFound:
                return BoardNotFound(id);
            case MutationError.Terminal:
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict, title: "Board is terminal",
                    detail: "This board has completed. Fetch its final state or upload a new board.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["boardStatus"] = result.State?.Status.ToString(),
                        ["finalStateUrl"] = $"/api/v1/boards/{id}",
                    });
            case MutationError.IterationLimit:
                int limit = options.Value.MaxFinalStateGenerations;
                LogNoFinalState(loggerFactory.CreateLogger(typeof(BoardEndpoints)), id, limit);
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity, title: "Board did not reach a final state",
                    detail: $"The board did not repeat an earlier generation within the limit of {limit} generations.",
                    extensions: new Dictionary<string, object?> { ["maxGenerations"] = limit });
            case MutationError.GenerationLimit:
                return GenerationLimit();
            case MutationError.BoardSizeLimit:
                return BoardSizeLimit();
            default:
                return TypedResults.Ok(BoardMapper.ToStateResponse(result.State
                    ?? throw new InvalidOperationException("A successful mutation must return a state.")));
        }
    }

    private static async Task<Results<Ok<BoardProjectionResponse>, ValidationProblem, ProblemHttpResult>> ProjectAsync(
        Guid id, int n, IBoardRepository repository, IOptions<GameOfLifeOptions> options, CancellationToken cancellationToken)
    {
        int limit = options.Value.MaxGenerationsAhead;
        if (n < 0 || n > limit)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["n"] = [$"The generation must be 0 to {limit}; this request asks for {n}."],
            });
        }

        var state = await repository.FindAsync(id, cancellationToken);
        if (state is null)
        {
            return BoardNotFound(id);
        }

        if (!options.Value.AllowsSimulation(state.Board))
        {
            return BoardSizeLimit();
        }

        if (state.Generation > long.MaxValue - n)
        {
            return GenerationLimit();
        }

        var board = Simulation.Advance(state.Board, n, cancellationToken);
        return TypedResults.Ok(new BoardProjectionResponse(id, state.Generation + n, board.Rows, board.Columns, BoardJson.ToRows(board), state.Generation));
    }

    private static ProblemHttpResult BoardNotFound(Guid id) => TypedResults.Problem(
        detail: $"No board is stored under the id {id}.", statusCode: StatusCodes.Status404NotFound, title: "Board not found");

    private static ProblemHttpResult GenerationLimit() => TypedResults.Problem(
        detail: "This operation would exceed the supported generation counter.",
        statusCode: StatusCodes.Status409Conflict, title: "Generation limit reached");

    private static ProblemHttpResult BoardSizeLimit() => TypedResults.Problem(
        detail: "The stored board exceeds the currently configured simulation dimensions.",
        statusCode: StatusCodes.Status422UnprocessableEntity, title: "Board exceeds simulation limits");

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored board {BoardId} of {Rows} x {Columns} cells")]
    private static partial void LogStored(ILogger logger, Guid boardId, int rows, int columns);

    [LoggerMessage(Level = LogLevel.Information, Message = "Board {BoardId} did not repeat an earlier generation within {MaxGenerations} generations")]
    private static partial void LogNoFinalState(ILogger logger, Guid boardId, int maxGenerations);
}