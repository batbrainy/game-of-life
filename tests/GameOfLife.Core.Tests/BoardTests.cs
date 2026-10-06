namespace GameOfLife.Core.Tests;

public sealed class BoardTests
{
    [Fact]
    public void TwoByThreeBoardReportsItsDimensionsAndCells()
    {
        var board = Board.FromCells(Pattern.Parse("#.#/.##"));

        Assert.Equal(2, board.Rows);
        Assert.Equal(3, board.Columns);
        Assert.Equal("#.#/.##", Pattern.Format(board));
    }

    [Theory]
    [InlineData("#")]
    [InlineData(".")]
    [InlineData("#../.#./..#")]
    [InlineData(".../.../..#")]
    [InlineData("###/###/##.")]
    public void OneByOneAndThreeByThreeBoardsReadBackCorrectly(string pattern)
    {
        var board = Board.FromCells(Pattern.Parse(pattern));

        Assert.Equal(pattern, Pattern.Format(board));
    }

    [Fact]
    public void FromCellsRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Board.FromCells(null!));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(0, 0)]
    public void FromCellsRejectsZeroRowsOrColumns(int rows, int columns)
    {
        Assert.Throws<ArgumentException>(() => Board.FromCells(new bool[rows, columns]));
    }

    [Theory]
    [InlineData(-1, 0, "row")]
    [InlineData(2, 0, "row")]
    [InlineData(0, -1, "column")]
    [InlineData(0, 3, "column")]
    public void ReadingOutsideTheGridThrows(int row, int column, string parameter)
    {
        var board = Board.FromCells(Pattern.Parse("#.#/.##"));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => board[row, column]);
        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public void BoardsFromEqualCellsAreEqualAndHaveEqualHashCodes()
    {
        var first = Board.FromCells(Pattern.Parse("#../.#./..#"));
        var second = Board.FromCells(Pattern.Parse("#../.#./..#"));

        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Theory]
    [InlineData("#.#/.##", "#.#/.#.")]
    [InlineData("#../.#./..#", "#../.#./...")]
    [InlineData("#.#/.##", "#./#./##")]
    public void BoardsDifferingInOneCellOrInShapeAreNotEqual(string pattern, string otherPattern)
    {
        var board = Board.FromCells(Pattern.Parse(pattern));
        var other = Board.FromCells(Pattern.Parse(otherPattern));

        Assert.False(board.Equals(other));
        Assert.False(board.Equals((object)other));
    }

    [Fact]
    public void BoardIsNotEqualToNull()
    {
        var board = Board.FromCells(Pattern.Parse("#"));

        Assert.False(board.Equals(null));
        Assert.False(board.Equals((object?)null));
    }

    [Fact]
    public void ChangingTheSourceArrayDoesNotChangeTheBoard()
    {
        var cells = Pattern.Parse("#.#/.##");
        var board = Board.FromCells(cells);

        cells[0, 0] = false;
        cells[0, 1] = true;

        Assert.Equal("#.#/.##", Pattern.Format(board));
    }

    [Fact]
    public void VerticalBlinkerGivesItsCellsRowByRow()
    {
        var board = Board.FromCells(Pattern.Parse(".#./.#./.#."));

        Assert.Equal<bool>([false, true, false, false, true, false, false, true, false], board.ToCellArray());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(8, 8)]
    [InlineData(5, 7)]
    [InlineData(16, 1)]
    public void BoardRebuiltFromItsCellArrayEqualsTheOriginal(int rows, int columns)
    {
        // A fixed seed gives the same "random" cells on every run.
        var random = new Random(42);
        var cells = new bool[rows, columns];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                cells[row, column] = random.Next(2) == 1;
            }
        }

        var board = Board.FromCells(cells);

        var rebuilt = Board.FromCellArray(rows, columns, board.ToCellArray());

        Assert.Equal(Pattern.Format(board), Pattern.Format(rebuilt));
        Assert.Equal(board, rebuilt);
    }

    [Theory]
    [InlineData(3, 3, 8)]
    [InlineData(3, 3, 10)]
    [InlineData(2, 3, 0)]
    [InlineData(65_536, 65_536, 0)]
    public void FromCellArrayRejectsAWrongLength(int rows, int columns, int length)
    {
        var exception = Assert.Throws<ArgumentException>(() => Board.FromCellArray(rows, columns, new bool[length]));
        Assert.Equal("cells", exception.ParamName);
    }

    [Theory]
    [InlineData(0, 3, "rows")]
    [InlineData(-1, 3, "rows")]
    [InlineData(3, 0, "columns")]
    [InlineData(3, -1, "columns")]
    public void FromCellArrayRejectsADimensionBelowOne(int rows, int columns, string parameter)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Board.FromCellArray(rows, columns, new bool[9]));
        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public void FromCellArrayRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Board.FromCellArray(3, 3, null!));
    }

    [Fact]
    public void ChangingTheArrayReturnedByToCellArrayDoesNotChangeTheBoard()
    {
        var board = Board.FromCells(Pattern.Parse("#.#/.##"));

        bool[] cells = board.ToCellArray();
        cells[0] = false;
        cells[1] = true;

        Assert.Equal("#.#/.##", Pattern.Format(board));
    }

    [Fact]
    public void ChangingTheArrayPassedToFromCellArrayDoesNotChangeTheBoard()
    {
        bool[] cells = [true, false, true, false, true, true];
        var board = Board.FromCellArray(2, 3, cells);

        cells[0] = false;
        cells[1] = true;

        Assert.Equal("#.#/.##", Pattern.Format(board));
    }
}