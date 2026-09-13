namespace Maran.Modules.Firewall.Domain.Entities;

/// <summary>
/// The record that the installer's whitelist seed has been read, which is what makes it read ONCE
/// rather than once per empty whitelist.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the seeded row cannot be its own marker.</b> The obvious gate — "seed while the whitelist
/// is empty" — is false the moment an administrator deletes the seeded row: the whitelist is empty
/// again, so the next restart puts the exemption back. That is not a tidiness point; the seed is
/// whatever address the installer arrived on, routinely a shared office NAT egress, a VPN
/// concentrator or a jump host — which is exactly WHY an operator revokes it — and
/// <c>panel.env</c> is never updated afterwards, so the value can name somebody else's address
/// months later. A revocation that comes back at the next reboot, with the journal showing only the
/// revocation, is a stranger becoming exempt from every automatic ban.
/// </para>
/// <para>
/// A row here survives that deletion, so the promise <c>panel.env</c> makes in as many words —
/// "read once; editing it afterwards changes nothing" — is held by the schema rather than by a
/// coincidence about the whitelist being non-empty.
/// </para>
/// </remarks>
public sealed class WhitelistSeedRecord
{
    /// <summary>The row's identity, fixed by the seeder so there can only ever be one.</summary>
    public Guid Id { get; private set; }

    /// <summary>The range that was seeded, in the form it was stored in.</summary>
    /// <remarks>
    /// Kept so an operator reading the table can see WHICH address was exempted on install day, long
    /// after the whitelist row itself has been edited or deleted.
    /// </remarks>
    public string Cidr { get; private set; }

    /// <summary>When the seed was read.</summary>
    public DateTimeOffset SeededAt { get; private set; }

    /// <summary>Records that the seed has been read.</summary>
    /// <param name="id">The row's identity, fixed by the seeder.</param>
    /// <param name="cidr">The range that was seeded.</param>
    /// <param name="seededAt">When it was read, taken from <see cref="IClock"/>.</param>
    public WhitelistSeedRecord(Guid id, string cidr, DateTimeOffset seededAt)
    {
        Id = id;
        Cidr = cidr;
        SeededAt = seededAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private WhitelistSeedRecord()
    {
        Cidr = string.Empty;
    }
}
