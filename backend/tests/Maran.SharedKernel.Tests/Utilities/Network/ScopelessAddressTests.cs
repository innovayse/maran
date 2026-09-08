using System.Net;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.SharedKernel.Tests.Utilities.Network;

/// <summary>
/// The removal of an IPv6 scope id, so the panel names a caller at the resolution its firewall can
/// act at.
/// </summary>
/// <remarks>
/// Named in no SharedKernel test before this file — only in a Firewall module test, which is the
/// wrong place for it to be held: the whole point of the type is that two components must reach the
/// same conclusion, and a test living inside one of them cannot say so.
/// </remarks>
public sealed class ScopelessAddressTests
{
    /// <summary>A scoped link local address comes back without its scope.</summary>
    [Fact]
    public void A_scoped_link_local_address_comes_back_without_its_scope()
    {
        var stripped = ScopelessAddress.Strip(IPAddress.Parse("fe80::1%3"));

        Assert.Equal("fe80::1", stripped.ToString());
        Assert.Equal(0, stripped.ScopeId);
    }

    /// <summary>Two scopes of one address collapse to the one subject a ban set can hold.</summary>
    [Fact]
    public void Two_scopes_of_one_address_collapse_to_the_one_subject_a_ban_set_can_hold()
    {
        // The deliberate merge, asserted rather than left implicit: these really are two machines,
        // and the panel gives them one name because a scopeless ban is the only response it has and
        // that ban blocks both regardless of which earned it.
        Assert.Equal(
            ScopelessAddress.Strip(IPAddress.Parse("fe80::1%3")).ToString(),
            ScopelessAddress.Strip(IPAddress.Parse("fe80::1%4")).ToString());
    }

    /// <summary>An unscoped ipv6 address is returned as the very same instance.</summary>
    [Fact]
    public void An_unscoped_ipv6_address_is_returned_as_the_very_same_instance()
    {
        var reported = IPAddress.Parse("2001:db8::1");

        Assert.Same(reported, ScopelessAddress.Strip(reported));
    }

    /// <summary>An ipv4 address is returned untouched rather than having its scope read.</summary>
    [Fact]
    public void An_ipv4_address_is_returned_untouched_rather_than_having_its_scope_read()
    {
        // Reading ScopeId on an IPv4 address THROWS rather than answering zero, so the family test
        // has to come first. Dropping it turns every IPv4 caller into an unhandled exception on the
        // path that records who they are.
        var reported = IPAddress.Parse("203.0.113.7");

        Assert.Same(reported, ScopelessAddress.Strip(reported));
    }

    /// <summary>The address bytes survive the strip so only the scope is lost.</summary>
    [Fact]
    public void The_address_bytes_survive_the_strip_so_only_the_scope_is_lost()
    {
        var reported = IPAddress.Parse("fe80::dead:beef:1:2%9");

        Assert.Equal(reported.GetAddressBytes(), ScopelessAddress.Strip(reported).GetAddressBytes());
    }

    /// <summary>A null address is refused at the call rather than dereferenced.</summary>
    [Fact]
    public void A_null_address_is_refused_at_the_call_rather_than_dereferenced()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            ScopelessAddress.Strip(null!);
        });
    }
}
