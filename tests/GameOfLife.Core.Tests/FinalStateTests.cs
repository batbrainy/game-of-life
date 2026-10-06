namespace GameOfLife.Core.Tests;

public sealed class FinalStateTests
{
    // Above every generation + period in the table below, so each of those searches finds its cycle.
    private const int Limit = 2000;

    private const string Glider = ".#./..#/###";
    private const string RPentomino = ".##/##./.#.";

    private const string Pulsar =
        "..###...###../" +
        "............./" +
        "#....#.#....#/" +
        "#....#.#....#/" +
        "#....#.#....#/" +
        "..###...###../" +
        "............./" +
        "..###...###../" +
        "#....#.#....#/" +
        "#....#.#....#/" +
        "#....#.#....#/" +
        "............./" +
        "..###...###..";

    [Theory]
    [InlineData(3, 3, 0, 0, ".", 0, 1, true)]
    [InlineData(4, 4, 1, 1, "##/##", 0, 1, false)]
    [InlineData(3, 3, 1, 1, "#", 1, 1, true)]
    [InlineData(3, 3, 0, 0, "###/###/###", 2, 1, true)]
    [InlineData(3, 3, 0, 1, "#/#/#", 0, 2, false)]
    [InlineData(1, 3, 0, 0, "###", 2, 1, true)]
    [InlineData(6, 6, 2, 1, ".###/###.", 0, 2, false)]
    [InlineData(6, 6, 1, 1, "##../##../..##/..##", 0, 2, false)]
    [InlineData(17, 17, 2, 2, Pulsar, 0, 3, false)]
    [InlineData(11, 18, 5, 4, "##########", 2, 15, false)]
    [InlineData(10, 10, 0, 0, Glider, 31, 1, false)]
    [InlineData(40, 40, 18, 16, "......#./##....../.#...###", 130, 1, true)]
    [InlineData(64, 64, 30, 30, RPentomino, 319, 2, false)]
    [InlineData(68, 68, 32, 32, RPentomino, 1163, 2, false)]
    public void FindsTheGenerationAndPeriodOfTheFirstRepeat(
        int rows, int columns, int top, int left, string pattern, int generation, int period, bool endsEmpty)
    {
        var board = Pattern.Place(rows, columns, top, left, pattern);

        var result = Simulation.FindFinalState(board, Limit, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(generation, result.Generation);
        Assert.Equal(period, result.Period);
        Assert.Equal(StepForward(board, generation), result.Board);
        Assert.Equal(endsEmpty, result.Board.ToCellArray().All(isAlive => !isAlive));
    }

    [Fact]
    public void GliderOnATenByTenBoardNeeds32GenerationsToBeSeenSettlingAsABlock()
    {
        var glider = Pattern.Place(10, 10, 0, 0, Glider);
        var block = Pattern.Place(10, 10, 8, 8, "##/##");

        Assert.Null(Simulation.FindFinalState(glider, 31, CancellationToken.None));
        Assert.Equal(new FinalState(block, 31, 1), Simulation.FindFinalState(glider, 32, CancellationToken.None));
    }

    [Fact]
    public void RPentominoOnA68By68BoardDoesNotRepeatWithin1000Generations()
    {
        var board = Pattern.Place(68, 68, 32, 32, RPentomino);

        Assert.Null(Simulation.FindFinalState(board, 1000, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LimitBelowOneThrows(int maxGenerations)
    {
        var board = Board.FromCells(Pattern.Parse("#"));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Simulation.FindFinalState(board, maxGenerations, CancellationToken.None));
        Assert.Equal("maxGenerations", exception.ParamName);
    }

    [Fact]
    public void AlreadyCancelledTokenThrows()
    {
        var board = Board.FromCells(Pattern.Parse("#/#/#"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => Simulation.FindFinalState(board, Limit, cancellation.Token));
    }

    [Fact]
    public void MatchesABruteForceSearchOn200RandomBoardsUpToSixBySix()
    {
        // A fixed seed gives the same "random" boards on every run.
        var random = new Random(42);
        int stoppedAtTheLimit = 0;

        for (int boardNumber = 0; boardNumber < 200; boardNumber++)
        {
            var cells = new bool[random.Next(1, 7), random.Next(1, 7)];
            for (int row = 0; row < cells.GetLength(0); row++)
            {
                for (int column = 0; column < cells.GetLength(1); column++)
                {
                    cells[row, column] = random.Next(2) == 1;
                }
            }

            var board = Board.FromCells(cells);
            // Limits from 1 to 30 stop some searches before a repeat, so both outcomes get compared.
            int maxGenerations = random.Next(1, 31);

            var expected = BruteForceFinalState(board, maxGenerations);
            Assert.Equal(expected, Simulation.FindFinalState(board, maxGenerations, CancellationToken.None));
            stoppedAtTheLimit += expected is null ? 1 : 0;
        }

        Assert.InRange(stoppedAtTheLimit, 1, 199);
    }

    private static Board StepForward(Board board, int generations)
    {
        for (int generation = 0; generation < generations; generation++)
        {
            board = board.Next();
        }

        return board;
    }

    // The obvious method, kept separate from the code under test: keep every state in a list and compare each new
    // state with all the earlier ones.
    private static FinalState? BruteForceFinalState(Board board, int maxGenerations)
    {
        var states = new List<Board> { board };
        for (int generation = 1; generation <= maxGenerations; generation++)
        {
            var next = states[^1].Next();
            for (int earlier = 0; earlier < states.Count; earlier++)
            {
                if (states[earlier].Equals(next))
                {
                    return new FinalState(next, earlier, generation - earlier);
                }
            }

            states.Add(next);
        }

        return null;
    }
}