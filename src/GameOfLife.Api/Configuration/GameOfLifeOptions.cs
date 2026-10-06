using System.ComponentModel.DataAnnotations;

namespace GameOfLife.Api.Configuration;

/// <summary>Limits on board size, simulation length and concurrent simulations, from the <c>GameOfLife</c> configuration section.</summary>
public sealed class GameOfLifeOptions
{
    public const string SectionName = "GameOfLife";

    [Range(1, int.MaxValue)]
    public int MaxRows { get; set; } = 256;

    [Range(1, int.MaxValue)]
    public int MaxColumns { get; set; } = 256;

    [Range(1, int.MaxValue)]
    public int MaxGenerationsAhead { get; set; } = 1000;

    [Range(1, int.MaxValue)]
    public int MaxFinalStateGenerations { get; set; } = 1000;

    /// <summary>The most simulations that run at once. Each one keeps a core busy, so the default is the number of processors.</summary>
    [Range(1, int.MaxValue)]
    public int MaxConcurrentSimulations { get; set; } = Environment.ProcessorCount;
}