using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Options;

namespace Maran.Modules.Marketplace.Tests.Domain.Policies;

/// <summary>The host allow-list: https, the same host name, the same port, no user information.</summary>
public sealed class MarketplaceHostPolicyTests
{
    /// <summary>The configured address the cases are judged against.</summary>
    private static readonly Uri Base = MarketplaceHostPolicy.BaseUri(new MarketplaceOptions { BaseUrl = "https://api.example.test" });

    /// <summary>Addresses on the configured host over https are accepted, whatever their path or the case of the host name.</summary>
    [Theory]
    [InlineData("https://api.example.test/a/b?x=1")]
    [InlineData("https://API.example.test/")]
    [InlineData("https://api.example.test:443/x")]
    public void An_address_on_the_configured_host_is_accepted(string address)
    {
        Assert.True(MarketplaceHostPolicy.IsConfiguredHost(new Uri(address), Base));
    }

    /// <summary>Another host, a lookalike, plain http, another port and user information are all refused.</summary>
    [Theory]
    [InlineData("https://evil.test/")]
    [InlineData("https://api.example.test.evil.test/")]
    [InlineData("https://xapi.example.test/")]
    [InlineData("http://api.example.test/")]
    [InlineData("https://api.example.test:8443/")]
    [InlineData("https://user@api.example.test/")]
    public void Any_other_address_is_refused(string address)
    {
        Assert.False(MarketplaceHostPolicy.IsConfiguredHost(new Uri(address), Base));
    }

    /// <summary>A relative address is never the configured host.</summary>
    [Fact]
    public void A_relative_address_is_refused()
    {
        Assert.False(MarketplaceHostPolicy.IsConfiguredHost(new Uri("/path", UriKind.Relative), Base));
    }
}
