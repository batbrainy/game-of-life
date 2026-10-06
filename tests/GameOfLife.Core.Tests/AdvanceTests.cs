namespace GameOfLife.Core.Tests;

public sealed class AdvanceTests
{
    private const string VerticalBlinker = ".#./.#./.#.";
    private const string HorizontalBlinker = ".../###/...";

    [Fact]
    public void ZeroGenerationsReturnsTheBoardAndOneReturnsItsNextGeneration()
    {
        var board = Board.FromCells(Pattern.Parse("#.#/.##/#.."));

        Assert.Equal(board, Simulation.Advance(board, 0, CancellationToken.None));
        Assert.Equal(board.Next(), Simulation.Advance(board, 1, CancellationToken.None));
    }

    [Theory]
    [InlineData(0, VerticalBlinker)]
    [InlineData(1, HorizontalBlinker)]
    [InlineData(2, VerticalBlinker)]
    [InlineData(3, HorizontalBlinker)]
    [InlineData(100, VerticalBlinker)]
    [InlineData(101, HorizontalBlinker)]
    public void BlinkerIsBackToItsStartAfterEvenCountsAndFlippedAfterOddCounts(int generations, string expected)
    {
        var blinker = Board.FromCells(Pattern.Parse(VerticalBlinker));

        var advanced = Simulation.Advance(blinker, generations, CancellationToken.None);

        Assert.Equal(expected, Pattern.Format(advanced));
    }

    [Fact]
    public void GliderOnATenByTenBoardIsABlockInTheCornerAfter31Generations()
    {
        var glider = Pattern.Place(10, 10, 0, 0, ".#./..#/###");
        var block = Pattern.Place(10, 10, 8, 8, "##/##");

        var advanced = Simulation.Advance(glider, 31, CancellationToken.None);

        Assert.Equal(Pattern.Format(block), Pattern.Format(advanced));
    }

    [Fact]
    public void NegativeCountThrows()
    {
        var board = Board.FromCells(Pattern.Parse(VerticalBlinker));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Simulation.Advance(board, -1, CancellationToken.None));
        Assert.Equal("generations", exception.ParamName);
    }

    [Fact]
    public void AlreadyCancelledTokenThrows()
    {
        var board = Board.FromCells(Pattern.Parse(VerticalBlinker));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => Simulation.Advance(board, 1, cancellation.Token));
    }
}