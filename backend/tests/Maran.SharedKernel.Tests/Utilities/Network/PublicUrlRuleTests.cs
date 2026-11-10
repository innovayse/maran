using Maran.SharedKernel.Utilities.Network;

namespace Maran.SharedKernel.Tests.Utilities.Network;

/// <summary>
/// What this panel accepts as its own public address: an https origin and nothing more. The
/// accepting cases are the inverse control of the refusing ones, so a rule mutated to refuse
/// everything fails here too.
/// </summary>
public sealed class PublicUrlRuleTests
{
    /// <summary>An https origin with a DNS name, an IP address or localhost is accepted and normalised.</summary>
    [Theory]
    [InlineData("https://panel.example.com", "https://panel.example.com")]
    [InlineData("https://panel.example.com:8443", "https://panel.example.com:8443")]
    [InlineData("https://panel.example.com:8443/", "https://panel.example.com:8443")]
    [InlineData("HTTPS://Panel.Example.COM:8443", "https://panel.example.com:8443")]
    [InlineData("https://panel.example.com:443", "https://panel.example.com")]
    [InlineData("https://203.0.113.7:8443", "https://203.0.113.7:8443")]
    [InlineData("https://[2001:db8::1]:8443", "https://[2001:db8::1]:8443")]
    [InlineData("https://localhost:5173", "https://localhost:5173")]
    public void An_https_origin_is_accepted_and_normalised(string value, string expected)
    {
        Assert.True(PublicUrlRule.TryNormalise(value, out var normalised));
        Assert.Equal(expected, normalised);
    }

    /// <summary>Anything that is not exactly an https origin is refused.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("panel.example.com")]
    [InlineData("http://panel.example.com")]
    [InlineData("ftp://panel.example.com")]
    [InlineData("https://")]
    [InlineData("https://user@panel.example.com")]
    [InlineData("https://user:pass@panel.example.com")]
    [InlineData("https://panel.example.com@evil.example")]
    [InlineData("https://panel.example.com/path")]
    [InlineData("https://panel.example.com/?a=b")]
    [InlineData("https://panel.example.com?a=b")]
    [InlineData("https://panel.example.com#frag")]
    [InlineData("https://panel.example.com//")]
    [InlineData("https://panel.example.com:0")]
    [InlineData("https://panel.example.com:65536")]
    [InlineData("https://panel.example.com:abc")]
    [InlineData("https://panel_host")]
    [InlineData("https://panel.example.com:")]
    [InlineData("https://[2001:db8::1]:")]
    [InlineData("https://b\u00fccher.example")]
    [InlineData("https://intranet")]
    [InlineData("https://[fe80::1%25eth0]:8443")]
    public void A_value_that_is_not_exactly_an_https_origin_is_refused(string? value)
    {
        Assert.False(PublicUrlRule.TryNormalise(value, out var normalised));
        Assert.Null(normalised);
    }

    /// <summary>A control character, whitespace or backslash anywhere is refused before the parser can drop it.</summary>
    [Theory]
    [InlineData("https://panel.example.com\n")]
    [InlineData("https://panel.example.com\r\nPanel__Other=1")]
    [InlineData("https://pa\nnel.example.com")]
    [InlineData("https://pa\tnel.example.com")]
    [InlineData("https://panel.example.com ")]
    [InlineData(" https://panel.example.com")]
    [InlineData("https://panel.example.com\\path")]
    [InlineData("https://panel.example.com\0")]
    public void A_control_character_whitespace_or_backslash_is_refused(string value)
    {
        Assert.False(PublicUrlRule.IsPublicUrl(value));
    }

    /// <summary>A value over the length ceiling is refused.</summary>
    [Fact]
    public void A_value_over_the_ceiling_is_refused()
    {
        var host = new string('a', PublicUrlRule.MaximumLength);

        Assert.False(PublicUrlRule.IsPublicUrl($"https://{host}.example.com"));
    }

    /// <summary>The host of an origin whose host is a DNS name is handed out lower-cased, port dropped.</summary>
    [Theory]
    [InlineData("https://panel.example.com", "panel.example.com")]
    [InlineData("HTTPS://Panel.Example.COM:8443/", "panel.example.com")]
    public void The_dns_host_of_an_acceptable_origin_is_handed_out(string value, string expected)
    {
        Assert.True(PublicUrlRule.TryGetDnsName(value, out var hostName));
        Assert.Equal(expected, hostName);
    }

    /// <summary>A refused origin, an address and localhost hand out no host name.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://panel.example.com")]
    [InlineData("https://user@panel.example.com")]
    [InlineData("https://panel.example.com/path")]
    [InlineData("https://203.0.113.7:8443")]
    [InlineData("https://[2001:db8::1]:8443")]
    [InlineData("https://localhost:8443")]
    public void No_host_name_is_handed_out_for_a_refused_origin_an_address_or_localhost(string? value)
    {
        Assert.False(PublicUrlRule.TryGetDnsName(value, out var hostName));
        Assert.Null(hostName);
    }
}
