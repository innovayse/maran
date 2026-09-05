using System.Net;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.SharedKernel.Tests.Utilities.Network;

/// <summary>The spelling this panel gives an address it is about to store, count, publish and ban.</summary>
public sealed class ClientAddressTests
{
    /// <summary>An ordinary IPv4 address is rendered as it stands.</summary>
    [Fact]
    public void An_ordinary_ipv4_address_is_rendered_as_it_stands()
    {
        Assert.Equal("203.0.113.7", ClientAddress.Of(IPAddress.Parse("203.0.113.7")));
    }

    /// <summary>An ipv4 address a dual stack listener reported as mapped is rendered as plain ipv4.</summary>
    [Fact]
    public void An_ipv4_address_a_dual_stack_listener_reported_as_mapped_is_rendered_as_plain_ipv4()
    {
        // The one case this type exists for. Two spellings of one address would split the
        // brute-force count in half, and the agent refuses to ban the mapped form at all — so a ban
        // built from it is a ban that matches no packet that ever arrives.
        Assert.Equal("203.0.113.7", ClientAddress.Of(IPAddress.Parse("::ffff:203.0.113.7")));
    }

    /// <summary>A real ipv6 address is left alone.</summary>
    [Fact]
    public void A_real_ipv6_address_is_left_alone()
    {
        Assert.Equal("2001:db8::1", ClientAddress.Of(IPAddress.Parse("2001:db8::1")));
    }

    /// <summary>A scoped ipv6 peer is rendered without its scope, because a ban set holds none.</summary>
    [Fact]
    public void A_scoped_ipv6_peer_is_rendered_without_its_scope_because_a_ban_set_holds_none()
    {
        // This type used to render the scope, on the reasoning that the same link-local address on
        // two interfaces is two machines. The reasoning was sound and the rule was not: the
        // Firewall module refuses to ban what it cannot install, so keeping the scope here made a
        // caller countable and unbannable. Nothing asserted the old behaviour, which is part of
        // why it survived.
        Assert.Equal("fe80::1", ClientAddress.Of(IPAddress.Parse("fe80::1%3")));
    }

    /// <summary>Two scopes of one address are rendered as the one subject the panel can ban.</summary>
    [Fact]
    public void Two_scopes_of_one_address_are_rendered_as_the_one_subject_the_panel_can_ban()
    {
        // The merge the losing argument warned about, asserted rather than left implicit: these
        // two really are different machines, and the panel deliberately gives them one name because
        // the only thing it can do about either — a scopeless ban — blocks both regardless.
        Assert.Equal(
            ClientAddress.Of(IPAddress.Parse("fe80::1%3")),
            ClientAddress.Of(IPAddress.Parse("fe80::1%4")));
    }

    /// <summary>A connection with no peer is rendered as the marker and never as an address.</summary>
    [Fact]
    public void A_connection_with_no_peer_is_rendered_as_the_marker_and_never_as_an_address()
    {
        Assert.Equal(ClientAddress.Unknown, ClientAddress.Of(null));
        Assert.False(IPAddress.TryParse(ClientAddress.Unknown, out _));
    }
}
