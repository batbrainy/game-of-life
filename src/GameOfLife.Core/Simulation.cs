namespace GameOfLife.Core;

/// <summary>Runs a board forward through several generations.</summary>
public static class Simulation
{
    /// <summary>Returns the board <paramref name="generations"/> steps after <paramref name="board"/>; 0 returns it unchanged.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before a generation was computed.</exception>
    public static Board Advance(Board board, int generations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentOutOfRangeException.ThrowIfNegative(generations);

        var current = board;
        for (int generation = 1; generation <= generations; generation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = current.Next();
        }

        return current;
    }

    /// <summary>
    /// Steps <paramref name="board"/> forward until a generation repeats an earlier one, at most
    /// <paramref name="maxGenerations"/> times.
    /// </summary>
    /// <returns>
    /// The first state of the cycle, or <see langword="null"/> when no generation repeated within the limit, which
    /// means a result exists only when its Generation + Period is at most <paramref name="maxGenerations"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException">The token was cancelled before a generation was computed.</exception>
    public static FinalState? FindFinalState(Board board, int maxGenerations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGenerations, 1);

        // Every board seen so far and the generation it first appeared at. Board compares by its cells, so a
        // repeat is found with one lookup instead of a comparison against every earlier generation.
        var firstSeenAt = new Dictionary<Board, int> { [board] = 0 };
        var current = board;
        for (int generation = 1; generation <= maxGenerations; generation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = current.Next();

            if (firstSeenAt.TryGetValue(current, out int cycleStart))
            {
                return new FinalState(current, cycleStart, generation - cycleStart);
            }

            firstSeenAt.Add(current, generation);
        }

        return null;
    }
}