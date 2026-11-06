using System.Net;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.SharedKernel.Tests.Utilities.Network;

/// <summary>
/// The strict range parser and the membership question. Host bits set are REFUSED, not normalised.
/// </summary>
public sealed class CidrRuleTests
{
    /// <summary>Parses text that must be valid, failing the test with the text when it is not.</summary>
    /// <param name="text">The rule text.</param>
    /// <returns>The parsed rule.</returns>
    private static CidrRule Rule(string text)
    {
        Assert.True(CidrRule.TryParse(text, out var rule), text);
        return rule!;
    }

    /// <summary>Valid IPv4 and IPv6 ranges parse and read back to their canonical text.</summary>
    [Theory]
    [InlineData("10.0.0.0/8", "10.0.0.0/8")]
    [InlineData("192.168.1.0/24", "192.168.1.0/24")]
    [InlineData("203.0.113.7/32", "203.0.113.7/32")]
    [InlineData("0.0.0.0/0", "0.0.0.0/0")]
    [InlineData("2001:db8::/32", "2001:db8::/32")]
    [InlineData("::/0", "::/0")]
    [InlineData("2001:db8::1/128", "2001:db8::1/128")]
    [InlineData("203.0.113.7", "203.0.113.7/32")]
    [InlineData("2001:db8::1", "2001:db8::1/128")]
    [InlineData("::ffff:10.0.0.0/104", "10.0.0.0/8")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7/32")]
    public void A_valid_range_parses_to_its_canonical_text(string text, string canonical)
    {
        Assert.Equal(canonical, Rule(text).ToString());
    }

    /// <summary>Malformed text is refused without throwing and yields no rule.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/")]
    [InlineData("/24")]
    [InlineData("10.0.0.0/")]
    [InlineData("10.0.0.0/24/8")]
    [InlineData(" 10.0.0.0/8")]
    [InlineData("10.0.0.0/8 ")]
    [InlineData("10.0.0.0 /8")]
    [InlineData("10.0.0")]
    [InlineData("10.0.0.0.0/8")]
    [InlineData("10.1/16")]
    [InlineData("256.0.0.0/8")]
    [InlineData("10.0.0.256")]
    [InlineData("010.0.0.0/8")]
    [InlineData("0x0a.0.0.0/8")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("10.0.0.0/+8")]
    [InlineData("10.0.0.0/08")]
    [InlineData("10.0.0.0/8a")]
    [InlineData("10.0.0.0/1000")]
    [InlineData("2001:db8::/129")]
    [InlineData("2001:db8::%eth0/64")]
    [InlineData("fe80::1%eth0")]
    [InlineData("2001:db8:::/32")]
    [InlineData("2001:zz8::/32")]
    [InlineData("example.com")]
    [InlineData("10.0.0.0\n")]
    [InlineData("::ffff:10.0.0.0/64")]
    public void Malformed_text_is_refused(string? text)
    {
        Assert.False(CidrRule.TryParse(text, out var rule));
        Assert.Null(rule);
    }

    /// <summary>A network address with host bits set is refused, in IPv4 and IPv6 alike.</summary>
    [Theory]
    [InlineData("10.0.0.5/24")]
    [InlineData("10.0.0.1/0")]
    [InlineData("192.168.1.129/25")]
    [InlineData("2001:db8::1/32")]
    [InlineData("::1/0")]
    [InlineData("::ffff:10.0.0.5/120")]
    public void A_network_with_host_bits_set_is_refused(string text)
    {
        Assert.False(CidrRule.TryParse(text, out _));
    }

    /// <summary>An IPv4 range contains its members and excludes its neighbours at the boundary.</summary>
    [Fact]
    public void An_ipv4_range_contains_its_members_and_not_its_neighbours()
    {
        var rule = Rule("192.168.1.0/24");

        Assert.True(rule.Contains(IPAddress.Parse("192.168.1.0")));
        Assert.True(rule.Contains(IPAddress.Parse("192.168.1.255")));
        Assert.False(rule.Contains(IPAddress.Parse("192.168.0.255")));
        Assert.False(rule.Contains(IPAddress.Parse("192.168.2.0")));
    }

    /// <summary>A prefix that is not a multiple of eight masks the partial byte.</summary>
    [Fact]
    public void A_partial_byte_prefix_masks_the_last_byte()
    {
        var rule = Rule("192.168.1.128/25");

        Assert.True(rule.Contains(IPAddress.Parse("192.168.1.128")));
        Assert.True(rule.Contains(IPAddress.Parse("192.168.1.255")));
        Assert.False(rule.Contains(IPAddress.Parse("192.168.1.127")));
        Assert.False(rule.Contains(IPAddress.Parse("192.168.1.0")));
    }

    /// <summary>A /32 matches exactly one address and a bare address means /32.</summary>
    [Fact]
    public void A_slash_32_and_a_bare_address_match_exactly_one_host()
    {
        var explicitRule = Rule("203.0.113.7/32");
        var bare = Rule("203.0.113.7");

        Assert.True(explicitRule.Contains(IPAddress.Parse("203.0.113.7")));
        Assert.False(explicitRule.Contains(IPAddress.Parse("203.0.113.8")));
        Assert.Equal(32, bare.PrefixLength);
        Assert.True(bare.Contains(IPAddress.Parse("203.0.113.7")));
        Assert.False(bare.Contains(IPAddress.Parse("203.0.113.6")));
    }

    /// <summary>A /0 matches every address of its own family and none of the other.</summary>
    [Fact]
    public void A_slash_0_matches_its_whole_family_only()
    {
        var v4 = Rule("0.0.0.0/0");
        var v6 = Rule("::/0");

        Assert.True(v4.Contains(IPAddress.Parse("198.51.100.9")));
        Assert.True(v4.Contains(IPAddress.Parse("::ffff:198.51.100.9")));
        Assert.False(v4.Contains(IPAddress.Parse("2001:db8::1")));
        Assert.True(v6.Contains(IPAddress.Parse("2001:db8::1")));
        Assert.False(v6.Contains(IPAddress.Parse("198.51.100.9")));
        Assert.Equal(0, v4.PrefixLength);
    }

    /// <summary>An IPv6 range contains its members and excludes the next block.</summary>
    [Fact]
    public void An_ipv6_range_contains_its_members_and_not_its_neighbours()
    {
        var rule = Rule("2001:db8::/32");

        Assert.True(rule.Contains(IPAddress.Parse("2001:db8::1")));
        Assert.True(rule.Contains(IPAddress.Parse("2001:db8:ffff:ffff:ffff:ffff:ffff:ffff")));
        Assert.False(rule.Contains(IPAddress.Parse("2001:db9::1")));
        Assert.False(rule.Contains(IPAddress.Parse("2001:db7:ffff::1")));
    }

    /// <summary>An IPv6 /128 and a bare IPv6 address match exactly one host.</summary>
    [Fact]
    public void An_ipv6_bare_address_matches_exactly_one_host()
    {
        var rule = Rule("2001:db8::1");

        Assert.Equal(128, rule.PrefixLength);
        Assert.True(rule.Contains(IPAddress.Parse("2001:db8::1")));
        Assert.False(rule.Contains(IPAddress.Parse("2001:db8::2")));
    }

    /// <summary>A dual-stack listener's mapped address matches an IPv4 rule (accepting control).</summary>
    [Fact]
    public void A_mapped_candidate_matches_an_ipv4_rule()
    {
        var rule = Rule("10.0.0.0/8");

        Assert.True(rule.Contains(IPAddress.Parse("::ffff:10.1.2.3")));
        Assert.False(rule.Contains(IPAddress.Parse("::ffff:11.1.2.3")));
    }

    /// <summary>A rule written in the mapped form behaves as the IPv4 rule it means, and not as an IPv6 one.</summary>
    [Fact]
    public void A_mapped_rule_is_an_ipv4_rule()
    {
        var rule = Rule("::ffff:10.0.0.0/104");

        Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, rule.Network.AddressFamily);
        Assert.True(rule.Contains(IPAddress.Parse("10.200.0.1")));
        Assert.True(rule.Contains(IPAddress.Parse("::ffff:10.200.0.1")));
        Assert.False(rule.Contains(IPAddress.Parse("2001:db8::1")));
    }

    /// <summary>A scope id on the candidate does not change the answer.</summary>
    [Fact]
    public void A_scope_id_on_the_candidate_is_ignored()
    {
        var rule = Rule("fe80::/10");
        var scoped = IPAddress.Parse("fe80::1");
        scoped.ScopeId = 2;

        Assert.True(rule.Contains(scoped));
    }
}
