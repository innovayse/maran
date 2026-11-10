using System.Net;
using Maran.Modules.ServerMigration.Domain.Policies;

namespace Maran.Modules.ServerMigration.Tests.Domain.Policies;

/// <summary>Which addresses the receiving panel refuses to connect to when it pulls a package (issue #127).</summary>
public sealed class PullAddressPolicyTests
{
    /// <summary>The addresses this server's own interfaces hold, in the tests below.</summary>
    private static readonly IReadOnlySet<IPAddress> Own = new HashSet<IPAddress> { IPAddress.Parse("203.0.113.10") };

    /// <summary>Loopback, unspecified, link-local, multicast and broadcast addresses, in both families, are refused.</summary>
    /// <param name="address">The address.</param>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.9.9.9")]
    [InlineData("0.0.0.0")]
    [InlineData("169.254.169.254")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    public void Addresses_that_cannot_be_another_server_are_refused(string address)
    {
        Assert.True(PullAddressPolicy.IsRefused(IPAddress.Parse(address), Own));
    }

    /// <summary>An IPv4 address wearing the mapped IPv6 form is judged as the IPv4 address it is.</summary>
    [Fact]
    public void A_mapped_loopback_is_refused_like_the_plain_one()
    {
        Assert.True(PullAddressPolicy.IsRefused(IPAddress.Parse("::ffff:127.0.0.1"), Own));
        Assert.True(PullAddressPolicy.IsRefused(IPAddress.Parse("::ffff:169.254.169.254"), Own));
    }

    /// <summary>An address one of this server's own interfaces holds is refused, mapped or not.</summary>
    [Fact]
    public void This_servers_own_address_is_refused()
    {
        Assert.True(PullAddressPolicy.IsRefused(IPAddress.Parse("203.0.113.10"), Own));
        Assert.True(PullAddressPolicy.IsRefused(IPAddress.Parse("::ffff:203.0.113.10"), Own));
    }

    /// <summary>The accepting controls: a public address and the private ranges are allowed on purpose.</summary>
    /// <param name="address">The address.</param>
    [Theory]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.11")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.4.4")]
    [InlineData("192.168.1.20")]
    [InlineData("fd00::5")]
    [InlineData("2001:db8::1")]
    public void A_public_or_private_address_is_allowed(string address)
    {
        Assert.False(PullAddressPolicy.IsRefused(IPAddress.Parse(address), Own));
    }
}
