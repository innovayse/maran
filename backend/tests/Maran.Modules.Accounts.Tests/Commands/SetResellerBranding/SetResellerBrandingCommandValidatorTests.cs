using Maran.Modules.Accounts.Commands.SetResellerBranding;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Commands.SetResellerBranding;

/// <summary>
/// Behavioral contract of <see cref="SetResellerBrandingCommandValidator"/>: each value is optional, and
/// each present one is refused with the code that says why.
/// </summary>
public sealed class SetResellerBrandingCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly SetResellerBrandingCommandValidator _validator = new();

    /// <summary>The codes a command is refused with.</summary>
    /// <param name="command">The command to validate.</param>
    private string[] CodesOf(SetResellerBrandingCommand command)
    {
        return _validator.Validate(command).Errors.Select(failure => { return failure.ErrorMessage; }).ToArray();
    }

    /// <summary>A command with all three valid values and one with none are both accepted: the inverse control.</summary>
    [Fact]
    public void Valid_values_and_no_values_are_accepted()
    {
        Assert.Empty(CodesOf(new SetResellerBrandingCommand("Acme", BrandingTestData.DarkEnoughColor, BrandingTestData.PngLogo)));
        Assert.Empty(CodesOf(new SetResellerBrandingCommand(null, null, null)));
    }

    /// <summary>A name over the limit or carrying a control character is refused, the limit itself is accepted.</summary>
    [Fact]
    public void A_name_over_the_limit_or_with_a_control_character_is_refused()
    {
        var longest = new string('a', ResellerBranding.DisplayNameMaxLength);

        Assert.Empty(CodesOf(new SetResellerBrandingCommand(longest, null, null)));
        Assert.Equal(
            "BrandingDisplayNameInvalid",
            Assert.Single(CodesOf(new SetResellerBrandingCommand(longest + "a", null, null))));
        Assert.Equal(
            "BrandingDisplayNameInvalid",
            Assert.Single(CodesOf(new SetResellerBrandingCommand("Ac\nme", null, null))));
    }

    /// <summary>A malformed colour and a too-light one are refused with different codes, one each.</summary>
    [Fact]
    public void A_malformed_colour_and_a_too_light_colour_are_refused_with_their_own_code()
    {
        Assert.Equal(
            "BrandingAccentColorInvalid",
            Assert.Single(CodesOf(new SetResellerBrandingCommand(null, "blue", null))));
        Assert.Equal(
            "BrandingAccentColorLowContrast",
            Assert.Single(CodesOf(new SetResellerBrandingCommand(null, BrandingTestData.TooLightColor, null))));
    }

    /// <summary>A logo that is not an accepted image and one that is too large are refused with their own code, one each.</summary>
    [Fact]
    public void A_logo_that_is_not_an_image_and_one_that_is_too_large_are_refused_with_their_own_code()
    {
        var oversized = BrandingTestData.DataUri(
            "image/png",
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[ResellerBranding.LogoMaxLength]).ToArray());

        Assert.Equal(
            "BrandingLogoInvalid",
            Assert.Single(CodesOf(new SetResellerBrandingCommand(null, null, "https://example.com/a.png"))));
        Assert.Equal(
            "BrandingLogoTooLarge",
            Assert.Single(CodesOf(new SetResellerBrandingCommand(null, null, oversized))));
    }
}
