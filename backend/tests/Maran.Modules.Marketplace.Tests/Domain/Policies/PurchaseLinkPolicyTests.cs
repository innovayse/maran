using Maran.Modules.Marketplace.Domain.Policies;

namespace Maran.Modules.Marketplace.Tests.Domain.Policies;

/// <summary>Only a plain https purchase address is shown; every other spelling is dropped.</summary>
public sealed class PurchaseLinkPolicyTests
{
    /// <summary>An https address passes unchanged.</summary>
    [Fact]
    public void A_plain_https_address_is_shown()
    {
        Assert.Equal("https://example.test/buy?module=mail", PurchaseLinkPolicy.HttpsOnly("https://example.test/buy?module=mail"));
    }

    /// <summary>A plain http address, a script address, a relative one, one with credentials, a missing one and an over-long one are dropped.</summary>
    /// <param name="url">The address.</param>
    [Theory]
    [InlineData("http://example.test/buy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/buy")]
    [InlineData("https://user:secret@example.test/buy")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_but_a_plain_https_address_is_dropped(string? url)
    {
        Assert.Null(PurchaseLinkPolicy.HttpsOnly(url));
    }

    /// <summary>An address longer than the limit is dropped; one at the limit passes.</summary>
    [Fact]
    public void An_over_long_address_is_dropped()
    {
        var atLimit = "https://example.test/" + new string('a', PurchaseLinkPolicy.MaxLength - "https://example.test/".Length);

        Assert.Equal(atLimit, PurchaseLinkPolicy.HttpsOnly(atLimit));
        Assert.Null(PurchaseLinkPolicy.HttpsOnly(atLimit + "a"));
    }
}
