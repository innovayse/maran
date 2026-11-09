using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Domain.Policies;

namespace Maran.Modules.Accounts.Tests.Domain.Policies;

/// <summary>What <see cref="BrandingAccentPolicy"/> accepts as the accent colour of a reseller's customers' shell.</summary>
public sealed class BrandingAccentPolicyTests
{
    /// <summary>A colour dark enough for white text is accepted, in either letter case: the accepting control.</summary>
    /// <param name="color">The colour.</param>
    [Theory]
    [InlineData("#2468dc")]
    [InlineData("#2468DC")]
    [InlineData("#000000")]
    [InlineData("#767676")]
    public void A_colour_that_carries_white_text_is_accepted(string color)
    {
        Assert.Equal(BrandingAccentVerdict.Acceptable, BrandingAccentPolicy.Check(color));
    }

    /// <summary>A colour too light for white text is refused as low contrast, and the line is the 4.5:1 one.</summary>
    /// <param name="color">The colour.</param>
    [Theory]
    [InlineData("#ffffff")]
    [InlineData("#ffcc00")]
    [InlineData("#777777")]
    [InlineData("#00ff00")]
    public void A_colour_too_light_for_white_text_is_refused_as_low_contrast(string color)
    {
        Assert.Equal(BrandingAccentVerdict.LowContrast, BrandingAccentPolicy.Check(color));
    }

    /// <summary>Anything that is not '#' plus six hexadecimal digits is refused as not a colour.</summary>
    /// <param name="color">The value.</param>
    [Theory]
    [InlineData("")]
    [InlineData("2468dc")]
    [InlineData("#246")]
    [InlineData("#2468dcff")]
    [InlineData("#2468dg")]
    [InlineData("#+468dc")]
    [InlineData("#24 8dc")]
    [InlineData("red")]
    public void A_value_that_is_not_a_hex_colour_is_refused(string color)
    {
        Assert.Equal(BrandingAccentVerdict.NotAColor, BrandingAccentPolicy.Check(color));
    }
}
