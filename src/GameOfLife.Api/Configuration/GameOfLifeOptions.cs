using System.ComponentModel.DataAnnotations;

namespace GameOfLife.Api.Configuration;

/// <summary>Limits on board size and simulation length, from the <c>GameOfLife</c> configuration section.</summary>
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
}