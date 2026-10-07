using GameOfLife.Core;

namespace GameOfLife.Api.Persistence;

public sealed record StoredBoard(
    Guid Id,
    Board Board,
    long Generation,
    BoardStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt = null,
    long? CycleStartGeneration = null,
    int? Period = null);