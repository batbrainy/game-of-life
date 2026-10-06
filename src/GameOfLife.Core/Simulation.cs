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
}