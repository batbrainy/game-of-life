using GameOfLife.Core;

namespace GameOfLife.Api.Persistence;

/// <summary>Stored boards. Each one is written once, under an id the caller chooses, and is never changed or deleted.</summary>
public interface IBoardRepository
{
    /// <summary>Stores <paramref name="board"/> under <paramref name="id"/>. An id that is already stored makes it throw; nothing is overwritten.</summary>
    Task AddAsync(Guid id, Board board, CancellationToken cancellationToken);

    /// <summary>Returns the board stored under <paramref name="id"/>, or <see langword="null"/> when there is none.</summary>
    Task<Board?> FindAsync(Guid id, CancellationToken cancellationToken);
}