namespace GameOfLife.Core;

public static class Simulation
{
    /// <summary>Returns the board <paramref name="generations"/> steps after <paramref name="board"/>; 0 returns it unchanged.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before a generation was computed.</exception>
    public static Board Advance(Board board, int generations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentOutOfRangeException.ThrowIfNegative(generations);

        var current = board;
        for (int generation = 0; generation < generations; generation++)
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
    /// means its GenerationsComputed is at most <paramref name="maxGenerations"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException">The token was cancelled before a generation was computed.</exception>
    public static FinalState? FindFinalState(Board board, int maxGenerations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGenerations, 1);

        cancellationToken.ThrowIfCancellationRequested();
        // Only fingerprints are retained. SHA-256 matching has negligible collision risk, not exact equality.
        var firstSeenAt = new Dictionary<string, int>(StringComparer.Ordinal) { [board.Fingerprint()] = 0 };
        var current = board;
        for (int step = 0; step < maxGenerations; step++)
        {
            int generation = step + 1;
            cancellationToken.ThrowIfCancellationRequested();
            current = current.Next();

            var fingerprint = current.Fingerprint();
            if (firstSeenAt.TryGetValue(fingerprint, out int cycleStart))
            {
                return new FinalState(current, cycleStart, generation - cycleStart);
            }

            firstSeenAt.Add(fingerprint, generation);
        }

        return null;
    }
}