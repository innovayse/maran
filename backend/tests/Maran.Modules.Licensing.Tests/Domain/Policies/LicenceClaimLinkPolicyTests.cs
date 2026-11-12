using Maran.Modules.Licensing.Domain.Policies;

namespace Maran.Modules.Licensing.Tests.Domain.Policies;

/// <summary>
/// Covers <see cref="LicenceClaimLinkPolicy"/>: which server ids and which base addresses make a link to the claim
/// page. Each refusing case sits beside the accepting control that proves the rule is not "never link".
/// </summary>
public sealed class LicenceClaimLinkPolicyTests
{
    /// <summary>A machine id as the marketplace spells one.</summary>
    private const string ServerId = "0123456789abcdef0123456789abcdef";

    /// <summary>The documented website.</summary>
    private const string Site = "https://plugins-maran.innovayse.com";

    /// <summary>The accepting control: a 32-digit id and an https origin make the link, with the id in the address.</summary>
    [Fact]
    public void A_well_formed_id_and_origin_make_the_claim_link()
    {
        Assert.Equal(
            "https://plugins-maran.innovayse.com/claim?server=" + ServerId,
            LicenceClaimLinkPolicy.Build(Site, ServerId));
    }

    /// <summary>A trailing slash on the configured origin and an upper-case id give the one spelling the marketplace stores.</summary>
    [Fact]
    public void The_link_has_one_spelling_whatever_the_case_or_the_trailing_slash()
    {
        Assert.Equal(
            "https://plugins-maran.innovayse.com/claim?server=" + ServerId,
            LicenceClaimLinkPolicy.Build(Site + "/", ServerId.ToUpperInvariant()));
    }

    /// <summary>An id the marketplace would refuse is not carried in a link.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("0123456789abcdef0123456789abcd&d")]
    public void An_id_that_is_not_32_hexadecimal_digits_makes_no_link(string? serverId)
    {
        Assert.Null(LicenceClaimLinkPolicy.Build(Site, serverId));
    }

    /// <summary>A base address that is not a plain https origin makes no link, so the screen never opens a site the reader did not see.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://plugins-maran.innovayse.com")]
    [InlineData("https://plugins-maran.innovayse.com@evil.example")]
    [InlineData("https://plugins-maran.innovayse.com/path")]
    [InlineData("https://plugins-maran.innovayse.com\n")]
    public void A_base_address_that_is_not_an_https_origin_makes_no_link(string? baseUrl)
    {
        Assert.Null(LicenceClaimLinkPolicy.Build(baseUrl, ServerId));
    }
}
