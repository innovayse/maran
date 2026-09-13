using System.Net;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Domain.ValueObjects;

namespace Maran.Modules.Firewall.Tests.Common;

/// <summary>
/// What a range recorded by the installer becomes before it is stored — the one boundary in this
/// module where a mapped spelling is translated instead of refused, because nobody is waiting on
/// the answer.
/// </summary>
public sealed class CidrRangeNormalizerTests
{
    /// <summary>The mapped form a dual stack sshd reports becomes the plain form that matches.</summary>
    [Theory]
    [InlineData("::ffff:203.0.113.7/128", "203.0.113.7/32")]
    [InlineData("::ffff:203.0.113.0/120", "203.0.113.0/24")]
    [InlineData("::ffff:198.51.100.0/118", "198.51.100.0/22")]
    public void The_mapped_form_a_dual_stack_sshd_reports_becomes_the_plain_form_that_matches(
        string cidr, string expected)
    {
        Assert.True(CidrRangeNormalizer.TryNormalize(cidr, out var normalized));
        Assert.Equal(expected, normalized);
    }

    /// <summary>The translated range matches exactly the addresses the mapped one named.</summary>
    [Fact]
    public void The_translated_range_matches_exactly_the_addresses_the_mapped_one_named()
    {
        // The measurement the whole type rests on: the mapped range parses and then matches nothing,
        // because every address it is compared against has been normalised to plain IPv4 and
        // IPNetwork.Contains is false across families. The translation is what makes it match.
        Assert.False(IPNetwork.Parse("::ffff:203.0.113.7/128").Contains(IPAddress.Parse("203.0.113.7")));

        Assert.True(CidrRangeNormalizer.TryNormalize("::ffff:203.0.113.7/128", out var normalized));

        Assert.True(IPNetwork.Parse(normalized).Contains(IPAddress.Parse("203.0.113.7")));
        Assert.False(IPNetwork.Parse(normalized).Contains(IPAddress.Parse("203.0.113.8")));
    }

    /// <summary>A plain range comes back in one spelling rather than as it was written.</summary>
    [Theory]
    [InlineData("203.0.113.7/32", "203.0.113.7/32")]
    [InlineData("0x7f.0.0.0/8", "127.0.0.0/8")]
    [InlineData("203.0.113.0/024", "203.0.113.0/24")]
    [InlineData("2001:DB8::/32", "2001:db8::/32")]
    public void A_plain_range_comes_back_in_one_spelling_rather_than_as_it_was_written(
        string cidr, string expected)
    {
        // One range has many spellings that all parse to the same network, and a row stored as
        // written is shown as one range and matched as another.
        Assert.True(CidrRangeNormalizer.TryNormalize(cidr, out var normalized));
        Assert.Equal(expected, normalized);
    }

    /// <summary>A mapped range shorter than the mapped block does not parse, so none arrives here.</summary>
    [Theory]
    [InlineData("::ffff:0:0/95")]
    [InlineData("::ffff:0:0/64")]
    [InlineData("::ffff:203.0.113.0/119")]
    public void A_mapped_range_shorter_than_the_mapped_block_does_not_parse_so_none_arrives_here(string cidr)
    {
        // This is why TryNormalize needs no rule for a prefix it could not translate honestly. The
        // block's own sixteen bits are set, so a shorter prefix leaves a host bit set and the
        // framework refuses the value before this type ever sees it — asserted rather than reasoned
        // about, because a guard nobody can reach would be decoration and the argument for leaving
        // it out has to be checkable.
        Assert.False(IPNetwork.TryParse(cidr, out _));

        Assert.False(CidrRangeNormalizer.TryNormalize(cidr, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    /// <summary>A value that is not a range in any spelling is refused.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("203.0.113.7")]
    [InlineData("203.0.113.7/24")]
    [InlineData("fe80::1%eth0/128")]
    [InlineData("fe80::1%3/128")]
    [InlineData("the office")]
    public void A_value_that_is_not_a_range_in_any_spelling_is_refused(string? cidr)
    {
        // A scope id is refused here as everywhere else: it names a link-local address that means a
        // different machine on a different interface, so the address without it is not the address.
        Assert.False(CidrRangeNormalizer.TryNormalize(cidr, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    /// <summary>Everything this returns is a range the rest of the module accepts.</summary>
    [Theory]
    [InlineData("::ffff:203.0.113.7/128")]
    [InlineData("203.0.113.0/024")]
    [InlineData("2001:db8::/32")]
    public void Everything_this_returns_is_a_range_the_rest_of_the_module_accepts(string cidr)
    {
        // The seeder stores the result without asking anything else about it, so a value that came
        // out of here and failed CidrRange.IsUsable would be a row the panel refuses to have written
        // itself.
        Assert.True(CidrRangeNormalizer.TryNormalize(cidr, out var normalized));
        Assert.True(CidrRange.IsUsable(normalized));
    }
}
