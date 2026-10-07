namespace GameOfLife.Core;

/// <summary>
/// Where a board's evolution starts repeating: <paramref name="Board"/> is first reached at <paramref name="CycleStartGeneration"/>
/// and comes back every <paramref name="Period"/> generations. Period 1 is a still life; a larger period is an oscillator.
/// </summary>
public sealed record FinalState(Board Board, int CycleStartGeneration, int Period)
{
    public int GenerationsComputed => checked(CycleStartGeneration + Period);
}