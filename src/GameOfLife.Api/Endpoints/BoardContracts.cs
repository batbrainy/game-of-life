namespace GameOfLife.Api.Endpoints;

/// <summary>The body of an upload: the grid as rows of cells, each 0 (dead) or 1 (alive).</summary>
public sealed record UploadBoardRequest(int[][]? Cells);

/// <summary>The body of a successful upload: the id the board is stored under.</summary>
public sealed record UploadBoardResponse(Guid Id);