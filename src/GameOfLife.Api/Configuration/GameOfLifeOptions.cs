using System.ComponentModel.DataAnnotations;

using GameOfLife.Core;

namespace GameOfLife.Api.Configuration;

public sealed class GameOfLifeOptions : IValidatableObject
{
    public const string SectionName = "GameOfLife";

    [Range(1, int.MaxValue)]
    public int MaxRows { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxColumns { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxGenerationsAhead { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxFinalStateGenerations { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxConcurrentSimulations { get; set; }

    [Range(1, int.MaxValue)]
    public int BoardLockTimeoutSeconds { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxBoardCells { get; set; }

    [Range(1, long.MaxValue)]
    public long MaxSimulationCellSteps { get; set; }

    [Range(1, long.MaxValue)]
    public long MaxRetainedStateBytes { get; set; }

    public bool AllowsSimulation(Board board) => board.Rows <= MaxRows && board.Columns <= MaxColumns;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        long cells = (long)MaxRows * MaxColumns;
        if (cells > Array.MaxLength)
        {
            yield return new ValidationResult("MaxRows * MaxColumns exceeds the runtime's maximum array length.",
                [nameof(MaxRows), nameof(MaxColumns)]);
            yield break;
        }

        if (cells > MaxBoardCells)
        {
            yield return new ValidationResult("MaxRows * MaxColumns must not exceed MaxBoardCells.",
                [nameof(MaxRows), nameof(MaxColumns), nameof(MaxBoardCells)]);
        }

        if (cells * Math.Max(MaxGenerationsAhead, MaxFinalStateGenerations) > MaxSimulationCellSteps)
        {
            yield return new ValidationResult("Board size times the larger iteration limit must not exceed MaxSimulationCellSteps.",
                [nameof(MaxGenerationsAhead), nameof(MaxFinalStateGenerations), nameof(MaxSimulationCellSteps)]);
        }

        // Starting, current and next int matrices, including estimated 64-bit row-array overhead;
        // a fingerprint JSON buffer; and 256 bytes per history entry for hex strings and dictionary storage.
        // Decimal prevents overflow. This is an estimate, not a total process-memory guarantee.
        decimal matrixBytes = (cells * sizeof(int)) + (32m * MaxRows) + 24;
        // Compact 0/1 JSON has length 2 * cells + 2 * rows + 1, including commas and brackets.
        decimal fingerprintBytes = (2 * cells) + (2m * MaxRows) + 1;
        decimal retainedBytes = ((3 * matrixBytes) + fingerprintBytes + (256m * (MaxFinalStateGenerations + 1m)))
            * MaxConcurrentSimulations;
        if (retainedBytes > MaxRetainedStateBytes)
        {
            yield return new ValidationResult("Concurrent simulation matrices and fingerprint history must not exceed MaxRetainedStateBytes.",
                [nameof(MaxFinalStateGenerations), nameof(MaxConcurrentSimulations), nameof(MaxRetainedStateBytes)]);
        }

        // CancellationTokenSource uses the runtime timer's unsigned-millisecond range; this is not a policy limit.
        if (TimeSpan.FromSeconds(BoardLockTimeoutSeconds).TotalMilliseconds > uint.MaxValue - 1L)
        {
            yield return new ValidationResult("BoardLockTimeoutSeconds exceeds the runtime timer's supported duration.",
                [nameof(BoardLockTimeoutSeconds)]);
        }

        ThreadPool.GetMaxThreads(out int maximumWorkers, out _);
        if ((long)MaxConcurrentSimulations + Environment.ProcessorCount > maximumWorkers)
        {
            yield return new ValidationResult("MaxConcurrentSimulations plus the processor count exceeds the runtime's maximum worker threads.",
                [nameof(MaxConcurrentSimulations)]);
        }
    }
}