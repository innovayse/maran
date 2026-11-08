using System.Net;
using Maran.Host.Demo.Pseudonymisation;

namespace Maran.Host.Demo.Tests.Pseudonymisation;

/// <summary>The network a visitor is counted as: an IPv4 address whole, an IPv6 address by its /64.</summary>
public sealed class VisitorNetworkTests
{
    /// <summary>The IPv4 address the tests use.</summary>
    private static readonly IPAddress Ipv4 = IPAddress.Parse("203.0.113.9");

    /// <summary>An IPv4 address, plain or mapped, is its own network, so two neighbours stay two visitors.</summary>
    [Fact]
    public void The_network_of_an_ipv4_address_is_the_address_itself()
    {
        Assert.Equal(Ipv4, VisitorNetwork.Of(Ipv4));
        Assert.Equal(Ipv4, VisitorNetwork.Of(Ipv4.MapToIPv6()));
        Assert.NotEqual(VisitorNetwork.Of(Ipv4), VisitorNetwork.Of(IPAddress.Parse("203.0.113.10")));
    }

    /// <summary>An IPv6 address loses its lower 64 bits.</summary>
    [Fact]
    public void The_network_of_an_ipv6_address_has_its_lower_half_cleared()
    {
        Assert.Equal(
            IPAddress.Parse("2001:db8:aaaa:bbbb::"),
            VisitorNetwork.Of(IPAddress.Parse("2001:db8:aaaa:bbbb:1:2:3:4")));
    }

    /// <summary>A scope id does not make a second network.</summary>
    [Fact]
    public void A_scope_id_is_dropped()
    {
        Assert.Equal(
            VisitorNetwork.Of(IPAddress.Parse("fe80::1")),
            VisitorNetwork.Of(IPAddress.Parse("fe80::2%3")));
    }
}
