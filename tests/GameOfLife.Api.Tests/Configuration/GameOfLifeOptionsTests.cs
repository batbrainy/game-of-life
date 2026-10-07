using System.ComponentModel.DataAnnotations;

using GameOfLife.Api.Configuration;

using Microsoft.Extensions.Configuration;

namespace GameOfLife.Api.Tests.Configuration;

public sealed class GameOfLifeOptionsTests
{
    [Fact]
    public void ShippedConfigurationIsValid() => Assert.Empty(Errors(Load()));

    [Theory]
    [InlineData("MaxGenerationsAhead", "20000")]
    [InlineData("MaxFinalStateGenerations", "20000")]
    [InlineData("MaxConcurrentSimulations", "17")]
    [InlineData("BoardLockTimeoutSeconds", "60")]
    public void FormerCeilingsDoNotOverrideConfiguredLimits(string name, string value)
    {
        var options = Load(("MaxRows", "1"), ("MaxColumns", "1"), (name, value));
        Assert.Empty(Errors(options));
    }

    [Theory]
    [InlineData("MaxRows")]
    [InlineData("MaxColumns")]
    [InlineData("MaxGenerationsAhead")]
    [InlineData("MaxFinalStateGenerations")]
    [InlineData("MaxConcurrentSimulations")]
    [InlineData("BoardLockTimeoutSeconds")]
    [InlineData("MaxBoardCells")]
    [InlineData("MaxSimulationCellSteps")]
    [InlineData("MaxRetainedStateBytes")]
    public void EverySettingMustBePositive(string name)
    {
        Assert.Contains(Errors(Load((name, "0"))), error => error.MemberNames.Contains(name));
    }

    [Theory]
    [InlineData("MaxBoardCells", "1023")]
    [InlineData("MaxSimulationCellSteps", "2047")]
    [InlineData("MaxRetainedStateBytes", "18312")]
    public void BudgetsAreConfigurableAndInclusive(string loweredBudget, string value)
    {
        (string, string)[] settings = [
            ("MaxRows", "32"), ("MaxColumns", "32"),
            ("MaxGenerationsAhead", "2"), ("MaxFinalStateGenerations", "2"), ("MaxConcurrentSimulations", "1"),
            ("MaxBoardCells", "1024"), ("MaxSimulationCellSteps", "2048"), ("MaxRetainedStateBytes", "18313"),
        ];
        Assert.Empty(Errors(Load(settings)));
        Assert.Contains(Errors(Load([.. settings, (loweredBudget, value)])), error => error.MemberNames.Contains(loweredBudget));
    }

    [Fact]
    public void LongSearchBudgetsFingerprintsRatherThanCompleteHistoricalMatrices()
    {
        // 3 working 256x256 int matrices plus 10,001 history entries fit comfortably in 4 MiB.
        var options = Load(("MaxFinalStateGenerations", "10000"), ("MaxConcurrentSimulations", "1"),
            ("MaxSimulationCellSteps", "655360000"), ("MaxRetainedStateBytes", "4194304"));
        Assert.Empty(Errors(options));
        options.MaxRetainedStateBytes = 2 * 1024 * 1024;
        Assert.Contains(Errors(options), error => error.MemberNames.Contains("MaxRetainedStateBytes"));
    }

    [Fact]
    public void LargeProductsAreRejectedWithoutArithmeticOverflow()
    {
        var options = Load(
            ("MaxRows", "1"), ("MaxColumns", "2000000000"), ("MaxBoardCells", "2147483647"),
            ("MaxFinalStateGenerations", "2147483647"), ("MaxConcurrentSimulations", "2147483647"),
            ("MaxSimulationCellSteps", "9223372036854775807"), ("MaxRetainedStateBytes", "9223372036854775807"));
        Assert.Contains(Errors(options), error => error.MemberNames.Contains("MaxRetainedStateBytes"));
    }

    [Fact]
    public void DimensionsMustFitTheEngineArray()
    {
        Assert.Contains(Errors(Load(("MaxRows", "2147483647"), ("MaxColumns", "2147483647"))),
            error => error.ErrorMessage?.Contains("array length") == true);
    }

    [Fact]
    public void LockTimeoutMustFitTheRuntimeTimer()
    {
        Assert.Contains(Errors(Load(("BoardLockTimeoutSeconds", "2147483647"))),
            error => error.MemberNames.Contains("BoardLockTimeoutSeconds"));
    }

    private static GameOfLifeOptions Load(params (string Name, string Value)[] settings)
    {
        using var configuration = new ConfigurationManager();
        configuration.SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json");
        foreach (var (name, value) in settings)
        {
            configuration[$"GameOfLife:{name}"] = value;
        }

        return configuration.GetRequiredSection(GameOfLifeOptions.SectionName).Get<GameOfLifeOptions>()
            ?? throw new InvalidOperationException("GameOfLife configuration is missing.");
    }

    private static List<ValidationResult> Errors(GameOfLifeOptions options)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), errors, validateAllProperties: true);
        return errors;
    }
}