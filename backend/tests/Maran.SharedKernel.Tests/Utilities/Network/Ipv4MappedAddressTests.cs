using System.Net;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.SharedKernel.Tests.Utilities.Network;

/// <summary>
/// The unwrapping of the IPv4-mapped IPv6 form, so one machine has one spelling everywhere in the
/// panel.
/// </summary>
/// <remarks>
/// This helper had no test of its own: what covered it was one assertion inside
/// <c>ClientAddressTests</c> and one inside a Firewall module test, each exercising it through a
/// different caller. That is coverage of the callers, not of the shared fact, and the shared fact
/// is the whole reason the type was lifted out of both of them.
/// </remarks>
public sealed class Ipv4MappedAddressTests
{
    /// <summary>A mapped address is unwrapped to the plain ipv4 the agent can ban.</summary>
    [Fact]
    public void A_mapped_address_is_unwrapped_to_the_plain_ipv4_the_agent_can_ban()
    {
        var unwrapped = Ipv4MappedAddress.Unwrap(IPAddress.Parse("::ffff:203.0.113.7"));

        Assert.Equal(IPAddress.Parse("203.0.113.7"), unwrapped);
        Assert.Equal("203.0.113.7", unwrapped.ToString());
    }

    /// <summary>An address already in plain ipv4 is returned as the very same instance.</summary>
    [Fact]
    public void An_address_already_in_plain_ipv4_is_returned_as_the_very_same_instance()
    {
        var reported = IPAddress.Parse("203.0.113.7");

        Assert.Same(reported, Ipv4MappedAddress.Unwrap(reported));
    }

    /// <summary>A genuine ipv6 address is returned as the very same instance.</summary>
    [Fact]
    public void A_genuine_ipv6_address_is_returned_as_the_very_same_instance()
    {
        var reported = IPAddress.Parse("2001:db8::1");

        Assert.Same(reported, Ipv4MappedAddress.Unwrap(reported));
    }

    /// <summary>The mapped and plain spellings of one machine come out as one address.</summary>
    [Fact]
    public void The_mapped_and_plain_spellings_of_one_machine_come_out_as_one_address()
    {
        // This is the property the type exists for, stated without reference to either caller: two
        // spellings of one machine would split a brute-force count in half, and a ban built from
        // the mapped form matches no packet that ever arrives.
        Assert.Equal(
            Ipv4MappedAddress.Unwrap(IPAddress.Parse("::ffff:198.51.100.9")),
            Ipv4MappedAddress.Unwrap(IPAddress.Parse("198.51.100.9")));
    }

    /// <summary>The ipv6 loopback is not mistaken for a mapped ipv4 loopback.</summary>
    [Fact]
    public void The_ipv6_loopback_is_not_mistaken_for_a_mapped_ipv4_loopback()
    {
        // ::1 and ::ffff:127.0.0.1 are both "loopback" to a reader and different addresses to a
        // ban set. An unwrap that reached for either loopback constant would pass every other test
        // in this file.
        Assert.Equal(IPAddress.IPv6Loopback, Ipv4MappedAddress.Unwrap(IPAddress.IPv6Loopback));
        Assert.Equal(IPAddress.Loopback, Ipv4MappedAddress.Unwrap(IPAddress.Parse("::ffff:127.0.0.1")));
    }
}
