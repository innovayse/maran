using Maran.SharedKernel.Utilities.Mail;

namespace Maran.SharedKernel.Tests.Utilities.Mail;

/// <summary>What the panel accepts as a bare e-mail address, in every field that holds one.</summary>
public sealed class EmailAddressRuleTests
{
    /// <summary>An ordinary address is accepted.</summary>
    [Theory]
    [InlineData("ops@example.com")]
    [InlineData("first.last+tag@sub.example.co.uk")]
    public void An_ordinary_address_is_accepted(string candidate)
    {
        Assert.True(EmailAddressRule.IsAddress(candidate));
    }

    /// <summary>A display-name form is refused, because it is two fields wearing one.</summary>
    /// <remarks>
    /// Accepting it would let a display name arrive through a field that does not validate display
    /// names — and a display name is exactly where a header-injecting newline would be aimed.
    /// </remarks>
    [Theory]
    [InlineData("Ops Team <ops@example.com>")]
    [InlineData("\"Ops\" <ops@example.com>")]
    public void A_display_name_wrapped_address_is_refused(string candidate)
    {
        Assert.False(EmailAddressRule.IsAddress(candidate));
    }

    /// <summary>A value carrying a newline is refused before anything tries to parse it.</summary>
    /// <remarks>
    /// rules/security.md item 4: a newline in a value bound for a header does not corrupt that
    /// header, it invents the next one.
    /// </remarks>
    /// <remarks>
    /// The last two cases are the ones that make <see cref="MailHeaderTextRule"/> a check rather
    /// than decoration, and they were missing until a mutation run said so. A quoted local part is
    /// legal address syntax, so <c>MailAddress</c> PARSES <c>"a\u0001b"@example.com</c> and hands it
    /// back unchanged — the round-trip test above sees nothing wrong with it. Only the control-
    /// character sweep refuses it. Without these rows, deleting the sweep broke no test at all.
    /// </remarks>
    [Theory]
    [InlineData("ops@example.com\r\nBcc: attacker@example.net")]
    [InlineData("ops@example.com\nBcc: attacker@example.net")]
    [InlineData("ops@example.com\0")]
    [InlineData("\"a\u0001b\"@example.com")]
    [InlineData("\"a\tb\"@example.com")]
    public void An_address_carrying_a_control_character_is_refused(string candidate)
    {
        Assert.False(EmailAddressRule.IsAddress(candidate));
    }

    /// <summary>Empty, whitespace and over-long values are refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("two@addresses.com, another@example.com")]
    public void A_value_that_is_not_one_address_is_refused(string candidate)
    {
        Assert.False(EmailAddressRule.IsAddress(candidate));
    }

    /// <summary>An address longer than the standard's ceiling is refused.</summary>
    [Fact]
    public void An_address_longer_than_the_standards_ceiling_is_refused()
    {
        var candidate = new string('a', EmailAddressRule.MaximumLength) + "@example.com";

        Assert.False(EmailAddressRule.IsAddress(candidate));
    }

    /// <summary>A null is refused rather than throwing, because a missing field is an invalid one.</summary>
    [Fact]
    public void A_null_is_refused()
    {
        Assert.False(EmailAddressRule.IsAddress(null));
    }
}
