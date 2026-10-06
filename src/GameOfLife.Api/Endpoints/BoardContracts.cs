namespace GameOfLife.Api.Endpoints;

/// <summary>The body of an upload: the grid as rows of cells, each 0 (dead) or 1 (alive).</summary>
public sealed record UploadBoardRequest(int[][]? Cells);

/// <summary>The body of a successful upload: the id the board is stored under.</summary>
public sealed record UploadBoardResponse(Guid Id);

/// <summary>A stored board at one generation: its size and its grid as rows of cells, each 0 (dead) or 1 (alive), as in an upload.</summary>
public sealed record BoardStateResponse(Guid Id, int Generation, int Rows, int Columns, int[][] Cells);

/// <summary>The final state of a stored board: <paramref name="Generation"/> is where its cycle starts and <paramref name="Period"/> the cycle's length, 1 meaning a still life.</summary>
public sealed record FinalStateResponse(Guid Id, int Generation, int Period, int Rows, int Columns, int[][] Cells);