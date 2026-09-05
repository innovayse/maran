using Maran.Modules.Firewall.Domain.ValueObjects;

namespace Maran.Modules.Firewall.Tests.Common;

/// <summary>Which strings this module will accept as an address range, and which it refuses.</summary>
public sealed class CidrRangeTests
{
    /// <summary>An ordinary range is usable.</summary>
    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("203.0.113.7/32")]
    [InlineData("203.0.113.0/24")]
    [InlineData("::/0")]
    [InlineData("2001:db8::/32")]
    [InlineData("2001:db8::7/128")]
    public void An_ordinary_range_is_usable(string cidr)
    {
        Assert.True(CidrRange.IsUsable(cidr));
    }

    /// <summary>A range with host bits beyond its prefix is refused rather than masked.</summary>
    [Theory]
    [InlineData("203.0.113.7/24")]
    [InlineData("10.0.0.1/8")]
    [InlineData("2001:db8::7/32")]
    public void A_range_with_host_bits_beyond_its_prefix_is_refused_rather_than_masked(string cidr)
    {
        // The two readings of 203.0.113.7/24 differ by a factor of two hundred and fifty-six in how
        // much of the internet they name. Refusing makes whoever wrote it say which they meant.
        Assert.False(CidrRange.IsUsable(cidr));
    }

    /// <summary>A range carrying a scope id is refused even though the framework parses it.</summary>
    [Theory]
    [InlineData("fe80::1%eth0/128")]
    [InlineData("fe80::1%3/128")]
    public void A_range_carrying_a_scope_id_is_refused_even_though_the_framework_parses_it(string cidr)
    {
        // Measured, not assumed, and the two forms differ: IPNetwork.TryParse accepts BOTH, keeping
        // the numeric scope and silently dropping the named one. So a row written as %eth0 would be
        // stored and displayed as one range and matched as another, and a row written as %3 could
        // only ever be compared against addresses IpAddressNormalizer has already refused a scope on.
        Assert.True(System.Net.IPNetwork.TryParse(cidr, out _));
        Assert.False(CidrRange.IsUsable(cidr));
    }

    /// <summary>A value that is not a range at all is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("203.0.113.7")]
    [InlineData("203.0.113.0/33")]
    [InlineData("the office")]
    public void A_value_that_is_not_a_range_at_all_is_refused(string cidr)
    {
        Assert.False(CidrRange.IsUsable(cidr));
    }

    /// <summary>An IPv4 mapped range is refused because it could never match anything.</summary>
    [Theory]
    [InlineData("::ffff:203.0.113.7/128")]
    [InlineData("::ffff:203.0.113.0/120")]
    [InlineData("::ffff:0:0/96")]
    public void An_IPv4_mapped_range_is_refused_because_it_could_never_match_anything(string cidr)
    {
        // The measurement that makes this a defect rather than a preference: the framework parses
        // the range happily, and the range then matches nothing, because IpAddressNormalizer has
        // already turned every address it will be compared against into plain IPv4 and
        // IPNetwork.Contains is false across families. The row was accepted, listed back to the
        // administrator verbatim, and exempted nobody.
        Assert.True(System.Net.IPNetwork.TryParse(cidr, out var parsed));
        Assert.False(parsed.Contains(System.Net.IPAddress.Parse("203.0.113.7")));

        Assert.False(CidrRange.IsUsable(cidr));
    }

    /// <summary>The plain form of the same range is accepted, so the refusal is not a dead end.</summary>
    [Fact]
    public void The_plain_form_of_the_same_range_is_accepted_so_the_refusal_is_not_a_dead_end()
    {
        // The mapped form is refused rather than rewritten, which is only defensible because the
        // administrator has somewhere to go: the plain spelling of the same intent works and does
        // match. Without this assertion the refusal above could be tightened into a rule that
        // rejects every IPv4 range and no test here would notice.
        Assert.True(CidrRange.IsUsable("203.0.113.7/32"));
        Assert.True(System.Net.IPNetwork.Parse("203.0.113.7/32")
            .Contains(System.Net.IPAddress.Parse("203.0.113.7")));
    }

    /// <summary>A missing value is an answer rather than a crash.</summary>
    [Fact]
    public void A_missing_value_is_an_answer_rather_than_a_crash()
    {
        // FluentValidation 12.1.1 runs a .Must(...) even after the .NotEmpty() before it has already
        // failed, so all three call sites reached here with null and turned a 400 into a 500.
        Assert.False(CidrRange.IsUsable(null));
    }
}
