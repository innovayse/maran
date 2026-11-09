using System.Net;
using Maran.Host.Demo.Pseudonymisation;

namespace Maran.Host.Demo.Tests.Pseudonymisation;

/// <summary>The stand-in keeps visitors apart and gives away nothing real.</summary>
public sealed class ClientPseudonymiserTests
{
    /// <summary>The real address the tests hide.</summary>
    private static readonly IPAddress Real = IPAddress.Parse("203.0.113.9");

    /// <summary>The same address gives the same stand-in under one key.</summary>
    [Fact]
    public void The_same_address_gives_the_same_stand_in()
    {
        var pseudonymiser = new ClientPseudonymiser();

        Assert.Equal(pseudonymiser.Of(Real), pseudonymiser.Of(IPAddress.Parse("203.0.113.9")));
    }

    /// <summary>The IPv4 address and its mapped IPv6 spelling are one visitor.</summary>
    [Fact]
    public void An_address_and_its_ipv4_mapped_spelling_are_one_visitor()
    {
        var pseudonymiser = new ClientPseudonymiser();

        Assert.Equal(pseudonymiser.Of(Real), pseudonymiser.Of(Real.MapToIPv6()));
    }

    /// <summary>Different addresses give different stand-ins.</summary>
    [Fact]
    public void Different_addresses_give_different_stand_ins()
    {
        var pseudonymiser = new ClientPseudonymiser();

        Assert.NotEqual(pseudonymiser.Of(Real), pseudonymiser.Of(IPAddress.Parse("203.0.113.10")));
    }

    /// <summary>A new key makes the same address a different stand-in, so intervals cannot be linked.</summary>
    [Fact]
    public void A_new_key_gives_a_different_stand_in_for_the_same_address()
    {
        Assert.NotEqual(new ClientPseudonymiser().Of(Real), new ClientPseudonymiser().Of(Real));
    }

    /// <summary>Two pseudonymisers over one key agree, so the key is what decides.</summary>
    [Fact]
    public void One_key_gives_one_answer()
    {
        var key = new byte[32];
        key[0] = 7;

        Assert.Equal(new ClientPseudonymiser(key).Of(Real), new ClientPseudonymiser(key).Of(Real));
    }

    /// <summary>The stand-in is under the documentation prefix and does not contain the real address.</summary>
    [Fact]
    public void The_stand_in_is_under_the_documentation_prefix_and_hides_the_real_address()
    {
        var standIn = new ClientPseudonymiser().Of(Real).ToString();

        Assert.StartsWith("2001:db8:", standIn, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.9", standIn, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two addresses of one IPv6 /64 are one visitor: a subscriber owning the network is not 2^64 visitors.
    /// </summary>
    [Fact]
    public void Two_addresses_of_one_ipv6_network_give_one_stand_in()
    {
        var pseudonymiser = new ClientPseudonymiser();

        Assert.Equal(
            pseudonymiser.Of(IPAddress.Parse("2001:db8:aaaa:bbbb::1")),
            pseudonymiser.Of(IPAddress.Parse("2001:db8:aaaa:bbbb:ffff:1234:5678:9abc")));
    }

    /// <summary>
    /// The control: addresses of two different /64 networks stay two visitors, a one-bit difference included.
    /// </summary>
    [Fact]
    public void Addresses_of_two_ipv6_networks_give_two_stand_ins()
    {
        var pseudonymiser = new ClientPseudonymiser();

        Assert.NotEqual(
            pseudonymiser.Of(IPAddress.Parse("2001:db8:aaaa:bbbb::1")),
            pseudonymiser.Of(IPAddress.Parse("2001:db8:aaaa:bbbc::1")));
    }
}
