namespace GameOfLife.Core.Tests;

public sealed class NextGenerationTests
{
    // The 8 neighbours of the centre of a 3x3 board, in the order the theory below makes them live.
    private static readonly (int Row, int Column)[] CentreNeighbours =
        [(0, 0), (0, 1), (0, 2), (1, 0), (1, 2), (2, 0), (2, 1), (2, 2)];

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(3, false, true)]
    [InlineData(4, false, false)]
    [InlineData(5, false, false)]
    [InlineData(6, false, false)]
    [InlineData(7, false, false)]
    [InlineData(8, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, true)]
    [InlineData(3, true, true)]
    [InlineData(4, true, false)]
    [InlineData(5, true, false)]
    [InlineData(6, true, false)]
    [InlineData(7, true, false)]
    [InlineData(8, true, false)]
    public void CentreCellWithKLiveNeighboursFollowsTheRules(int liveNeighbours, bool startsAlive, bool aliveAfterwards)
    {
        var cells = new bool[3, 3];
        cells[1, 1] = startsAlive;
        foreach (var (row, column) in CentreNeighbours.Take(liveNeighbours))
        {
            cells[row, column] = true;
        }

        var next = Board.FromCells(cells).Next();

        Assert.Equal(aliveAfterwards, next[1, 1]);
    }

    [Fact]
    public void AllAliveThreeByThreeBecomesTheFourCornersOnly()
    {
        var next = Board.FromCells(Pattern.Parse("###/###/###")).Next();

        Assert.Equal("#.#/.../#.#", Pattern.Format(next));
    }

    [Fact]
    public void BlockOnAFourByFourBoardIsUnchanged()
    {
        var next = Board.FromCells(Pattern.Parse("..../.##./.##./....")).Next();

        Assert.Equal("..../.##./.##./....", Pattern.Format(next));
    }

    [Fact]
    public void VerticalBlinkerBecomesHorizontalAndBackAgain()
    {
        var horizontal = Board.FromCells(Pattern.Parse(".#./.#./.#.")).Next();

        Assert.Equal(".../###/...", Pattern.Format(horizontal));
        Assert.Equal(".#./.#./.#.", Pattern.Format(horizontal.Next()));
    }

    [Fact]
    public void GliderMovesOneRowDownAndOneColumnRightInFourSteps()
    {
        var glider = WithLiveCells(10, 10, (0, 1), (1, 2), (2, 0), (2, 1), (2, 2));
        var moved = WithLiveCells(10, 10, (1, 2), (2, 3), (3, 1), (3, 2), (3, 3));

        var afterFourSteps = glider.Next().Next().Next().Next();

        Assert.Equal(Pattern.Format(moved), Pattern.Format(afterFourSteps));
    }

    [Theory]
    [InlineData("#", ".")]
    [InlineData("###", ".#.")]
    public void PositionsOutsideTheGridCountAsDead(string pattern, string expected)
    {
        var next = Board.FromCells(Pattern.Parse(pattern)).Next();

        Assert.Equal(expected, Pattern.Format(next));
    }

    [Fact]
    public void BoardNextWasCalledOnIsUnchanged()
    {
        var board = Board.FromCells(Pattern.Parse(".#./.#./.#."));

        board.Next();

        Assert.Equal(".#./.#./.#.", Pattern.Format(board));
    }

    private static Board WithLiveCells(int rows, int columns, params (int Row, int Column)[] liveCells)
    {
        var cells = new bool[rows, columns];
        foreach (var (row, column) in liveCells)
        {
            cells[row, column] = true;
        }

        return Board.FromCells(cells);
    }
}