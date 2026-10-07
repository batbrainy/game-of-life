using System.Text.Json.Serialization;

namespace GameOfLife.Api.Endpoints;

public sealed record UploadBoardRequest(int[][]? Cells);

public sealed record UploadBoardResponse(Guid Id);

public sealed record BoardStateResponse(
    Guid Id, long Generation, int Rows, int Columns, int[][] Cells, string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CycleStartGeneration = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Period = null);

public sealed record BoardProjectionResponse(Guid Id, long Generation, int Rows, int Columns, int[][] Cells, long SourceGeneration);