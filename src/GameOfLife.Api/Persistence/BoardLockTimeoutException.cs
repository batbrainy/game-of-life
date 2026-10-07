namespace GameOfLife.Api.Persistence;

public sealed class BoardLockTimeoutException : TimeoutException
{
    public BoardLockTimeoutException()
        : base("Timed out waiting for the board's mutation lock.")
    {
    }
}