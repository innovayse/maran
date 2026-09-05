using System.Net;

namespace Maran.Modules.Firewall.Domain.Entities;

/// <summary>
/// One address range the panel's automatic bans never touch (spec §15). The operator's own office,
/// a monitoring probe, an office VPN.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is an exemption from AUTOMATIC bans, not from the firewall.</b> A whitelisted range is
/// not allowed through anything; it is only never banned by the brute-force detector. An
/// administrator can still ban it deliberately, and the journal records that they did — which is the
/// right split, because the danger this list exists to prevent is a mistyped password locking the
/// only person who could undo it out of the server.
/// </para>
/// <para>
/// <b>The range is stored in one spelling, and matched by parsing that.</b> Every path that creates
/// a row goes through <c>CidrRange</c> — <c>IsUsable</c> to refuse an entry that would look like an
/// exemption while being none, and <c>Canonical</c> to decide the spelling — so the range an
/// administrator reads back is the range that is matched. Storing the text as typed had the two
/// disagreeing: <c>0x7f.0.0.0/8</c> was listed as itself and matched as <c>127.0.0.0/8</c>, and two
/// spellings of one range were two rows.
/// </para>
/// </remarks>
public sealed class WhitelistEntry
{
    /// <summary>The row's identity, and the only identifier a request may name.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The exempt range in CIDR notation — <c>203.0.113.7/32</c>, <c>2001:db8::/32</c>.
    /// </summary>
    /// <remarks>
    /// Host bits beyond the prefix are refused rather than masked away when the row is created. The
    /// two readings of <c>203.0.113.7/24</c> — one host, or two hundred and fifty-six of them —
    /// differ by the whole blast radius of the exemption, and silently picking one is how an
    /// operator exempts a neighbour's machine believing they exempted their own.
    /// </remarks>
    public string Cidr { get; private set; }

    /// <summary>What the range is, in the administrator's own words, so a later reader knows why it is here.</summary>
    public string Note { get; private set; }

    /// <summary>When the row was added.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records an exempt range.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="cidr">The exempt range in CIDR notation, already validated.</param>
    /// <param name="note">What the range is, in the administrator's own words.</param>
    /// <param name="createdAt">When the row was added, taken from <see cref="IClock"/>.</param>
    public WhitelistEntry(Guid id, string cidr, string note, DateTimeOffset createdAt)
    {
        Id = id;
        Cidr = cidr;
        Note = note;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private WhitelistEntry()
    {
        Cidr = string.Empty;
        Note = string.Empty;
    }

    /// <summary>Whether <paramref name="address"/> falls inside this range.</summary>
    /// <param name="address">The address a ban is about to be placed on.</param>
    /// <returns>True when the range covers it; false when it does not, or when the row cannot be parsed.</returns>
    /// <remarks>
    /// An unparseable row answers false rather than throwing. It cannot exempt anything either way —
    /// the validator refuses such a value at creation — and a whitelist that threw would turn one bad
    /// row into a detector that stops banning ANY address, which is the failure this list would cause
    /// rather than prevent.
    ///
    /// An IPv4 range never covers an IPv6 address and the reverse, which is <see cref="IPNetwork"/>'s
    /// own rule and the right one: the two families are different address spaces, and a v4 exemption
    /// that silently covered a v6 peer would be an exemption nobody wrote.
    /// </remarks>
    public bool Covers(IPAddress address)
    {
        return IPNetwork.TryParse(Cidr, out var network) && network.Contains(address);
    }
}
