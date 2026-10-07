using GameOfLife.Api.Configuration;
using GameOfLife.Api.Persistence;
using GameOfLife.Core;

using Microsoft.Extensions.Options;

namespace GameOfLife.Api.Boards;

public enum MutationError
{
    None,
    NotFound,
    Terminal,
    IterationLimit,
    GenerationLimit,
    BoardSizeLimit,
}

public sealed record MutationResult(StoredBoard? State, MutationError Error = MutationError.None);

public sealed class BoardService(IBoardRepository repository, IOptions<GameOfLifeOptions> options)
{
    public async Task<MutationResult> AdvanceAsync(Guid id, bool toFinalState, CancellationToken cancellationToken)
    {
        await using var session = await repository.LockAsync(
            id, TimeSpan.FromSeconds(options.Value.BoardLockTimeoutSeconds), cancellationToken);
        var state = await session.FindAsync(cancellationToken);
        if (state is null)
        {
            return new(null, MutationError.NotFound);
        }

        if (state.Status != BoardStatus.Active)
        {
            return new(state, toFinalState ? MutationError.None : MutationError.Terminal);
        }

        int limit = toFinalState ? options.Value.MaxFinalStateGenerations : 1;
        if (!options.Value.AllowsSimulation(state.Board))
        {
            return new(state, MutationError.BoardSizeLimit);
        }

        if (state.Generation > long.MaxValue - limit)
        {
            return new(state, MutationError.GenerationLimit);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!toFinalState)
        {
            var saved = await session.SaveAsync(state.Board.Next(), state.Generation + 1, BoardStatus.Active, null, null, cancellationToken);
            return new(saved);
        }

        var final = Simulation.FindFinalState(state.Board, limit, cancellationToken);
        if (final is null)
        {
            return new(state, MutationError.IterationLimit);
        }

        var completed = await session.SaveAsync(
            final.Board, state.Generation + final.GenerationsComputed,
            final.Period == 1 ? BoardStatus.Stable : BoardStatus.Cycle,
            state.Generation + final.CycleStartGeneration, final.Period, cancellationToken);
        return new(completed);
    }
}