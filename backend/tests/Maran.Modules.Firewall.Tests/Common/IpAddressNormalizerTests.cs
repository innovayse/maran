using System.Net;
using Maran.Modules.Firewall.Domain.Policies;

namespace Maran.Modules.Firewall.Tests.Common;

/// <summary>
/// Which spelling of an address leaves this module, which is the difference between a ban that
/// drops packets and one that silently drops none.
/// </summary>
public sealed class IpAddressNormalizerTests
{
    /// <summary>An ipv4 mapped ipv6 address is normalised to plain ipv4.</summary>
    [Fact]
    public void An_ipv4_mapped_ipv6_address_is_normalised_to_plain_ipv4()
    {
        // The whole reason this type exists. A dual-stack listener reports an IPv4 peer this way,
        // and the agent refuses it deliberately: a mapped address in the IPv6 ban set matches no
        // IPv4 packet, so accepting it would be a ban that does nothing at all.
        var normalised = IpAddressNormalizer.TryNormalize("::ffff:203.0.113.7", out var address);

        Assert.True(normalised);
        Assert.Equal("203.0.113.7", address.ToString());
    }

    /// <summary>A plain ipv4 address is left exactly as it was.</summary>
    [Fact]
    public void A_plain_ipv4_address_is_left_exactly_as_it_was()
    {
        var normalised = IpAddressNormalizer.TryNormalize("203.0.113.7", out var address);

        Assert.True(normalised);
        Assert.Equal("203.0.113.7", address.ToString());
    }

    /// <summary>A real ipv6 address is not turned into an ipv4 one.</summary>
    [Fact]
    public void A_real_ipv6_address_is_not_turned_into_an_ipv4_one()
    {
        var normalised = IpAddressNormalizer.TryNormalize("2001:db8::7", out var address);

        Assert.True(normalised);
        Assert.Equal("2001:db8::7", address.ToString());
    }

    /// <summary>An address carrying a scope id is stripped to the address the agent can ban.</summary>
    [Fact]
    public void An_address_carrying_a_scope_id_is_stripped_to_the_address_the_agent_can_ban()
    {
        // This assertion was inverted, and deliberately. It used to demand a refusal, on the
        // reasoning that a ban set holds no scope so there is nothing to install. True, and still
        // the wrong half to hold firm: ClientAddress kept the scope and the detector counted under
        // it, so refusing here produced a caller counted to the threshold and never banned. The
        // panel now names a caller at the one resolution its ban set can express.
        var normalised = IpAddressNormalizer.TryNormalize("fe80::1%3", out var address);

        Assert.True(normalised);
        Assert.Equal("fe80::1", address.ToString());
        Assert.Equal(0u, address.ScopeId);
    }

    /// <summary>A value that is not an address at all is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("203.0.113.7/32")]
    public void A_value_that_is_not_an_address_at_all_is_refused(string reported)
    {
        Assert.False(IpAddressNormalizer.TryNormalize(reported, out _));
    }

    /// <summary>A named scope id reaches the same address as the numeric spelling of it.</summary>
    [Fact]
    public void A_named_scope_id_reaches_the_same_address_as_the_numeric_spelling_of_it()
    {
        // Kept, re-pointed. It used to guard the pre-parse text check, and it still guards the
        // thing that mattered: the framework treats the two spellings differently, so a rule
        // written for one silently misses the other. Measured here rather than assumed — the
        // framework PARSES the named form and throws the name away, leaving ScopeId zero, while
        // the numeric form survives into ScopeId. Stripping AFTER the parse is what makes one
        // rule cover both, where the old text check covered only the spelling nobody tested.
        Assert.True(IPAddress.TryParse("fe80::1%eth0", out var parsedByFramework));
        Assert.Equal(0u, parsedByFramework.ScopeId);

        Assert.True(IpAddressNormalizer.TryNormalize("fe80::1%eth0", out var named));
        Assert.True(IpAddressNormalizer.TryNormalize("fe80::1%3", out var numeric));
        Assert.Equal("fe80::1", named.ToString());
        Assert.Equal(named.ToString(), numeric.ToString());
    }

    /// <summary>A scope id with no address in front of it is still not an address.</summary>
    [Theory]
    [InlineData("%3")]
    [InlineData("203.0.113.7%3")]
    public void A_scope_id_with_no_address_in_front_of_it_is_still_not_an_address(string reported)
    {
        // The strip must not become a way to smuggle a non-address past the parse. Neither of
        // these parses at all — a scope belongs to IPv6 and to nothing else — so both are refused
        // as what they are rather than salvaged into something bannable.
        Assert.False(IpAddressNormalizer.TryNormalize(reported, out var address));
        Assert.Equal(IPAddress.None, address);
    }

    /// <summary>A mapped address wearing a scope id comes out as plain ipv4.</summary>
    [Fact]
    public void A_mapped_address_wearing_a_scope_id_comes_out_as_plain_ipv4()
    {
        // Both normalisations at once. The ordering between them is a preference, not a constraint:
        // ScopelessAddress.Strip guards on the address family before reading ScopeId, so it cannot
        // throw on IPv4 and the reverse order yields the same 203.0.113.7. What this case actually
        // pins is that the two normalisations COMPOSE — a mapped address wearing a scope loses both
        // wrappings, in whichever order they are applied.
        var normalised = IpAddressNormalizer.TryNormalize("::ffff:203.0.113.7%3", out var address);

        Assert.True(normalised);
        Assert.Equal("203.0.113.7", address.ToString());
    }
}
