using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Persistence;

namespace Maran.Modules.Firewall.Queries.ListBans;

/// <summary>
/// Handles <see cref="ListBansQuery"/> by reading <c>firewall.BanEpisodes</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The agent's own ban listing is deliberately not called here.</b> What the kernel holds is an
/// address and a countdown; the column an operator opens this screen for is <c>Reason</c>, and the
/// agent stores none — it cannot, because the only place one could go on that side is an nftables
/// comment whose argument <c>nft</c> parses in its own grammar. These rows are the only record of
/// why anybody was banned, so they are what the screen shows.
/// </para>
/// <para>
/// Expired and lifted episodes are filtered out here rather than deleted from the table: they are
/// what the escalation ladder counts, so a listing is a view of the history and not the history
/// itself.
/// </para>
/// </remarks>
public sealed class ListBansQueryHandler
{
    /// <summary>The Firewall module's database context, which is the durable store of every ban.</summary>
    private readonly FirewallDbContext _dbContext;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Firewall module's database context.</param>
    /// <param name="clock">The panel's clock, which decides what has run out.</param>
    public ListBansQueryHandler(FirewallDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Returns the bans still in force, newest first.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the bans; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<BanDto>>> HandleAsync(
        ListBansQuery query,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var candidates = await _dbContext.BanEpisodes
            .AsNoTracking()
            .Where(episode => episode.LiftedAt == null && (episode.ExpiresAt == null || episode.ExpiresAt > now))
            .OrderByDescending(episode => episode.BannedAt)
            .ToListAsync(cancellationToken);

        // The final say is the entity's, not the WHERE clause's: an episode with less than a whole
        // second left cannot be re-applied and is therefore already over, and only BanEpisode knows
        // that rule. The clause above is what keeps the query from reading the whole table.
        var bans = candidates
            .Where(episode =>
            {
                return episode.IsInForce(now);
            })
            .Select(episode =>
            {
                return new BanDto(
                    episode.Id,
                    episode.IpAddress,
                    episode.Reason,
                    episode.Failures,
                    episode.BannedAt,
                    episode.ExpiresAt);
            })
            .ToList();

        return Result<IReadOnlyList<BanDto>>.Ok(bans);
    }
}
