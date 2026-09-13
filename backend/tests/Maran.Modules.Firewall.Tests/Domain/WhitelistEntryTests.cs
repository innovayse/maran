using System.Net;
using Maran.Modules.Firewall.Domain.Entities;

namespace Maran.Modules.Firewall.Tests.Domain;

/// <summary>Which addresses one whitelist row actually exempts.</summary>
public sealed class WhitelistEntryTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A single host range covers exactly that host.</summary>
    [Fact]
    public void A_single_host_range_covers_exactly_that_host()
    {
        var entry = Entry("203.0.113.7/32");

        Assert.True(entry.Covers(IPAddress.Parse("203.0.113.7")));
        Assert.False(entry.Covers(IPAddress.Parse("203.0.113.8")));
    }

    /// <summary>A network range covers every address inside it.</summary>
    [Fact]
    public void A_network_range_covers_every_address_inside_it()
    {
        var entry = Entry("203.0.113.0/24");

        Assert.True(entry.Covers(IPAddress.Parse("203.0.113.1")));
        Assert.True(entry.Covers(IPAddress.Parse("203.0.113.255")));
        Assert.False(entry.Covers(IPAddress.Parse("203.0.114.1")));
    }

    /// <summary>An ipv4 range never covers an ipv6 address.</summary>
    [Fact]
    public void An_ipv4_range_never_covers_an_ipv6_address()
    {
        // Two address spaces. A v4 exemption that silently covered a v6 peer would be an exemption
        // nobody wrote — and the office arriving over IPv6 would still be banned, which is the
        // failure this row exists to prevent.
        Assert.False(Entry("0.0.0.0/0").Covers(IPAddress.Parse("2001:db8::7")));
    }

    /// <summary>An ipv6 range covers an ipv6 address inside it.</summary>
    [Fact]
    public void An_ipv6_range_covers_an_ipv6_address_inside_it()
    {
        Assert.True(Entry("2001:db8::/32").Covers(IPAddress.Parse("2001:db8::7")));
    }

    /// <summary>A row that cannot be parsed covers nothing rather than throwing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("203.0.113.7")]
    [InlineData("203.0.113.7/24")]
    [InlineData("not a range")]
    public void A_row_that_cannot_be_parsed_covers_nothing_rather_than_throwing(string cidr)
    {
        // Such a row cannot exempt anything either way — the validator refuses it on the way in —
        // and a whitelist that threw would turn one bad row into a detector that stops banning ANY
        // address. Note 203.0.113.7/24: host bits beyond the prefix are refused, never masked,
        // because the two readings differ by a factor of two hundred and fifty-six.
        Assert.False(Entry(cidr).Covers(IPAddress.Parse("203.0.113.7")));
    }

    /// <summary>Builds a whitelist row for <paramref name="cidr"/>.</summary>
    /// <param name="cidr">The range the row exempts.</param>
    private static WhitelistEntry Entry(string cidr)
    {
        return new WhitelistEntry(Guid.NewGuid(), cidr, "office", CreatedAt);
    }
}
