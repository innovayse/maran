using Maran.Modules.Monitoring.Commands.SaveAlertThresholds;

namespace Maran.Modules.Monitoring.Tests.Commands.SaveAlertThresholds;

/// <summary>What saving the alert thresholds accepts and what it refuses.</summary>
public sealed class SaveAlertThresholdsCommandValidatorTests
{
    /// <summary>Whole numbers inside the bounds, and an unwatched processor and memory, are accepted.</summary>
    [Theory]
    [InlineData(50, 50, 50)]
    [InlineData(90, null, null)]
    [InlineData(99, 99, null)]
    public void Figures_inside_the_bounds_and_an_unwatched_condition_are_accepted(int disk, int? cpu, int? memory)
    {
        var result = new SaveAlertThresholdsCommandValidator().Validate(new SaveAlertThresholdsCommand(disk, cpu, memory));

        Assert.True(result.IsValid);
    }

    /// <summary>A figure outside the bounds is refused with the module's code, whichever threshold carries it.</summary>
    [Theory]
    [InlineData(49, null, null)]
    [InlineData(100, null, null)]
    [InlineData(90, 49, null)]
    [InlineData(90, 100, null)]
    [InlineData(90, null, 49)]
    [InlineData(90, null, 100)]
    public void A_figure_outside_the_bounds_is_refused_with_the_modules_code(int disk, int? cpu, int? memory)
    {
        var result = new SaveAlertThresholdsCommandValidator().Validate(new SaveAlertThresholdsCommand(disk, cpu, memory));

        var failure = Assert.Single(result.Errors);
        Assert.Equal("AlertThresholdOutOfRange", failure.ErrorCode);
    }
}
