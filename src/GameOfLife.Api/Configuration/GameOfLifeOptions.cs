using System.ComponentModel.DataAnnotations;

using GameOfLife.Core;

namespace GameOfLife.Api.Configuration;

/// <summary>Limits on board size, simulation length and concurrent simulations, from the <c>GameOfLife</c> configuration section.</summary>
public sealed class GameOfLifeOptions : IValidatableObject
{
    public const string SectionName = "GameOfLife";

    [Range(1, 1024)]
    public int MaxRows { get; set; } = 256;

    [Range(1, 1024)]
    public int MaxColumns { get; set; } = 256;

    [Range(1, 10000)]
    public int MaxGenerationsAhead { get; set; } = 500;

    [Range(1, 10000)]
    public int MaxFinalStateGenerations { get; set; } = 500;

    [Range(1, 16)]
    public int MaxConcurrentSimulations { get; set; } = Math.Min(Environment.ProcessorCount, 8);

    [Range(1, 30)]
    public int BoardLockTimeoutSeconds { get; set; } = 5;

    public bool AllowsSimulation(Board board) => board.Rows <= MaxRows && board.Columns <= MaxColumns;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        long cells = (long)MaxRows * MaxColumns;
        if (cells > 65536)
        {
            yield return new ValidationResult("MaxRows * MaxColumns must not exceed 65536 cells.", [nameof(MaxRows), nameof(MaxColumns)]);
        }

        if (cells * Math.Max(MaxGenerationsAhead, MaxFinalStateGenerations) > 64L * 1024 * 1024)
        {
            yield return new ValidationResult("The maximum board size and iteration limits must not exceed 67108864 cell-steps per simulation.",
                [nameof(MaxGenerationsAhead), nameof(MaxFinalStateGenerations)]);
        }

        // Includes retained states and conversion buffers; the process still needs memory for HTTP, the pool and GC.
        if (cells * (MaxFinalStateGenerations + 8L) * MaxConcurrentSimulations > 512L * 1024 * 1024)
        {
            yield return new ValidationResult("Concurrent final-state buffers must not exceed the 512 MiB budget.", [nameof(MaxConcurrentSimulations)]);
        }
    }
}