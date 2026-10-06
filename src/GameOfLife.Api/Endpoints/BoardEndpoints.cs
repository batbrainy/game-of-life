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

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored board {BoardId} of {Rows} x {Columns} cells")]
    private static partial void LogStored(ILogger logger, Guid boardId, int rows, int columns);
}