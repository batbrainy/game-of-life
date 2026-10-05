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
}