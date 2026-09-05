using System.Net;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Persistence;

namespace Maran.Modules.Firewall.Services;

/// <summary>
/// The one place this module asks whether an address is exempt from an AUTOMATIC ban.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists as a type rather than as two lines in each caller.</b> Ruling R8 says the
/// whitelist is checked panel-side before every automatic ban. It was — in
/// <see cref="IntegrationEvents.Handlers.BruteForceDetectedHandler"/> — and it was not in
/// <see cref="StartupBanReconciler"/>, which re-applies every episode still in force after a
/// restart without consulting the list at all. So an operator could be whitelisted and banned at
/// the same time: the exemption held while the panel ran and was ignored the moment it came back.
/// The harm was bounded by the original TTL, but "bounded" is not the promise the list makes.
/// </para>
/// <para>
/// <b>Three paths reach <c>BanAsync</c>, and only two of them come through here.</b> The detector's
/// handler asks before placing a new ban; the reconciler asks before restoring an old one. The third
/// is <see cref="Commands.BanAddress.BanAddressCommandHandler"/> — an administrator banning an
/// address deliberately — and it does NOT consult this guard, on purpose: the whitelist exempts an
/// address from the detector, not from an administrator who has decided to block it
/// (<see cref="WhitelistEntry"/> says so in as many words). A FOURTH path that reaches
/// <c>BanAsync</c> automatically without coming through here is the original defect again; the
/// guard shrinks the number of places that must remember from every caller to this one, and that is
/// the honest description of what it achieves.
/// </para>
/// <para>
/// <b>Two ways in, because the two callers ask at different rates.</b> A detection is one event, so
/// the handler asks <see cref="ExemptsAsync"/> and pays for one small read — invisible beside the
/// twenty-five Argon2id verifications the detection cost the attacker, and worth it because a row
/// added a second ago is then honoured by the next ban rather than by the next restart. A
/// reconciliation pass is one event over N items, so the reconciler takes <see cref="SnapshotAsync"/>
/// once and matches with <see cref="Exempts"/>: the per-call read there would be N sequential round
/// trips before a single ban is re-installed, and the freshness it buys is worthless inside a pass
/// no administrator can edit the whitelist in the middle of.
/// </para>
/// </remarks>
public sealed class WhitelistGuard
{
    /// <summary>The module's database, holding the whitelist rows.</summary>
    private readonly FirewallDbContext _dbContext;

    /// <summary>Creates the guard.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public WhitelistGuard(FirewallDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Whether any whitelist row covers <paramref name="address"/>.</summary>
    /// <param name="address">The address an automatic ban is about to be placed on.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>True when the address is exempt and must not be banned.</returns>
    /// <remarks>
    /// One read per question, which is the right trade where the question is asked once per event.
    /// A caller asking it per ITEM wants <see cref="SnapshotAsync"/> instead.
    /// </remarks>
    public async Task<bool> ExemptsAsync(IPAddress address, CancellationToken cancellationToken)
    {
        return Exempts(await SnapshotAsync(cancellationToken), address);
    }

    /// <summary>Reads the whole whitelist once, for a caller that will ask about many addresses.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every exempt range, as rows rather than as an answer.</returns>
    public async Task<IReadOnlyList<WhitelistEntry>> SnapshotAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.WhitelistEntries.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <summary>Whether any row of <paramref name="whitelist"/> covers <paramref name="address"/>.</summary>
    /// <param name="whitelist">The rows, from <see cref="SnapshotAsync"/>.</param>
    /// <param name="address">The address an automatic ban is about to be placed on.</param>
    /// <returns>True when the address is exempt and must not be banned.</returns>
    /// <remarks>
    /// The matching rule lives here and in no other method, so the answer cannot differ between the
    /// caller that reads per event and the caller that reads per pass. It is done in memory rather
    /// than filtered in SQL because the question is CIDR containment and the database holds the
    /// ranges as text; the list is an operator-sized table, a handful of rows.
    /// </remarks>
    public static bool Exempts(IReadOnlyList<WhitelistEntry> whitelist, IPAddress address)
    {
        return whitelist.Any(entry =>
        {
            return entry.Covers(address);
        });
    }
}
