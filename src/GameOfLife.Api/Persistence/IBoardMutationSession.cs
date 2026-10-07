using GameOfLife.Core;

namespace GameOfLife.Api.Persistence;

public interface IBoardMutationSession : IAsyncDisposable
{
    Task<StoredBoard?> FindAsync(CancellationToken cancellationToken);

    Task<StoredBoard> SaveAsync(
        Board board, long generation, BoardStatus status, long? cycleStartGeneration, int? period,
        CancellationToken cancellationToken);
}