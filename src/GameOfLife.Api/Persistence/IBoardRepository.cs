using GameOfLife.Core;

namespace GameOfLife.Api.Persistence;

/// <summary>Current snapshots; mutations must use a session that owns the board's distributed lock.</summary>
public interface IBoardRepository
{
    /// <summary>Stores <paramref name="board"/> under <paramref name="id"/>. An id that is already stored makes it throw; nothing is overwritten.</summary>
    Task AddAsync(Guid id, Board board, CancellationToken cancellationToken);

    /// <summary>Returns the board stored under <paramref name="id"/>, or <see langword="null"/> when there is none.</summary>
    Task<StoredBoard?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IBoardMutationSession> LockAsync(Guid id, TimeSpan timeout, CancellationToken cancellationToken);
}