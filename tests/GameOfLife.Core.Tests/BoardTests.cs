namespace GameOfLife.Core.Tests;

public sealed class BoardTests
{
    [Theory]
    [InlineData("#")]
    [InlineData(".")]
    [InlineData("#.#/.##")]
    [InlineData("#../.#./..#")]
    public void MatrixPreservesDimensionsAndCellOrder(string pattern)
    {
        var cells = Pattern.Parse(pattern);
        var board = Board.FromMatrix(cells);

        Assert.Equal(cells.Length, board.Rows);
        Assert.Equal(cells[0].Length, board.Columns);
        Assert.Equal(pattern, Pattern.Format(board));
        Assert.Equal(cells, board.ToMatrix());
    }

    [Fact]
    public void FromMatrixRejectsNull() => Assert.Throws<ArgumentNullException>(() => Board.FromMatrix(null!));

    public static TheoryData<int[][]> InvalidMatrices => new()
    {
        Array.Empty<int[]>(),
        new int[][] { [] },
        new int[][] { null! },
        new int[][] { [1], null! },
        new int[][] { [0], [0, 1] },
        new int[][] { [0, 2] },
        new int[][] { [-1] },
    };

    [Theory]
    [MemberData(nameof(InvalidMatrices))]
    public void FromMatrixRejectsInvalidShapeOrValues(int[][] cells) =>
        Assert.Throws<ArgumentException>(() => Board.FromMatrix(cells));

    [Theory]
    [InlineData(-1, 0, "row")]
    [InlineData(2, 0, "row")]
    [InlineData(0, -1, "column")]
    [InlineData(0, 3, "column")]
    public void ReadingOutsideTheGridThrows(int row, int column, string parameter)
    {
        var board = Board.FromMatrix(Pattern.Parse("#.#/.##"));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => board[row, column]);
        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public void SourceAndExportedMatricesCannotMutateTheBoardOrItsNextGeneration()
    {
        var cells = Pattern.Parse(".#./.#./.#.");
        var board = Board.FromMatrix(cells);
        var fingerprint = board.Fingerprint();
        var next = board.Next();

        cells[0][1] = 0;
        cells[1] = [1, 1, 1];
        var exported = board.ToMatrix();
        exported[2][1] = 0;
        exported[0] = [1, 1, 1];
        var nextExported = next.ToMatrix();
        nextExported[1][0] = 0;

        Assert.Equal(".#./.#./.#.", Pattern.Format(board));
        Assert.Equal(".../###/...", Pattern.Format(next));
        Assert.Equal(fingerprint, board.Fingerprint());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(8, 8)]
    [InlineData(5, 7)]
    [InlineData(16, 1)]
    public void RandomMatrixRoundTripPreservesEveryCell(int rows, int columns)
    {
        var random = new Random(42);
        var cells = Enumerable.Range(0, rows)
            .Select(_ => Enumerable.Range(0, columns).Select(_ => random.Next(2)).ToArray()).ToArray();
        var board = Board.FromMatrix(cells);
        var rebuilt = Board.FromMatrix(board.ToMatrix());

        Assert.Equal(cells, rebuilt.ToMatrix());
    }

    [Fact]
    public void CellCountValidationDoesNotOverflow()
    {
        // Shared rows keep this invalid 2^32-cell input small enough to test without allocating the grid.
        var cells = Enumerable.Repeat(new int[65_536], 65_536).ToArray();

        Assert.Throws<ArgumentException>(() => Board.FromMatrix(cells));
    }

    [Fact]
    public void RepeatedRowReferencesAreCopiedIndependently()
    {
        int[] row = [0, 1];
        var board = Board.FromMatrix([row, row]);
        var exported = board.ToMatrix();
        exported[0][0] = 1;
        row[1] = 0;

        Assert.Equal(0, exported[1][0]);
        Assert.Equal(".#/.#", Pattern.Format(board));
    }
}