using System.ComponentModel.DataAnnotations;
using Maran.Modules.Identity.Options;

namespace Maran.Modules.Identity.Tests.Options;

/// <summary>The sub-user cap is validated the way the host validates it at startup, so a nonsense value stops the boot.</summary>
public sealed class SubUserOptionsTests
{
    /// <summary>Runs the data-annotation validation <c>ValidateDataAnnotations</c> runs at startup.</summary>
    /// <param name="options">The options to validate.</param>
    /// <returns><c>true</c> when the options pass.</returns>
    private static bool IsValid(SubUserOptions options)
    {
        return Validator.TryValidateObject(options, new ValidationContext(options), null, validateAllProperties: true);
    }

    /// <summary>The shipped default is a valid cap.</summary>
    [Fact]
    public void The_default_cap_is_valid()
    {
        Assert.True(IsValid(new SubUserOptions()));
    }

    /// <summary>A cap of zero would silently switch the feature off while the screen offers it, so it is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1_001)]
    public void A_cap_outside_the_range_is_refused(int cap)
    {
        Assert.False(IsValid(new SubUserOptions { MaxPerAccount = cap }));
    }

    /// <summary>The bounds themselves are accepted (the inverse control for the refusals above).</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1_000)]
    public void A_cap_at_the_bounds_is_accepted(int cap)
    {
        Assert.True(IsValid(new SubUserOptions { MaxPerAccount = cap }));
    }
}
