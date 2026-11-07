using Maran.Modules.Identity.Commands.SaveAuditRetention;

namespace Maran.Modules.Identity.Tests.Commands.SaveAuditRetention;

/// <summary>Behavioural contract of the audit-retention validator.</summary>
public sealed class SaveAuditRetentionCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly SaveAuditRetentionCommandValidator _validator = new();

    /// <summary>The window's edges are accepted: the floor, the ceiling, and "keep indefinitely".</summary>
    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(365)]
    [InlineData(3650)]
    [InlineData(null)]
    public void A_window_inside_the_bounds_or_null_is_accepted(int? days)
    {
        Assert.True(_validator.Validate(new SaveAuditRetentionCommand(days)).IsValid);
    }

    /// <summary>Anything outside the bounds is refused with the out-of-range code, including "delete everything now".</summary>
    [Theory]
    [InlineData(29)]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    [InlineData(int.MaxValue)]
    public void A_window_outside_the_bounds_is_refused_with_the_out_of_range_code(int days)
    {
        var result = _validator.Validate(new SaveAuditRetentionCommand(days));

        Assert.False(result.IsValid);
        Assert.Equal("AuditRetentionOutOfRange", Assert.Single(result.Errors).ErrorMessage);
    }
}
