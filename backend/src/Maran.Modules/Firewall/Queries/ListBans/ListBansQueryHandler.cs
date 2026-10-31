using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Persistence;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Firewall.Queries.ListBans;

/// <summary>
/// Handles <see cref="ListBansQuery"/> by reading one page of <c>firewall.BanEpisodes</c>.
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
/// itself. The filter is <see cref="BanEpisode.InForceAt"/>, the entity's own rule as a predicate,
/// so the count and the page are taken over exactly the episodes <see cref="BanEpisode.IsInForce"/>
/// would keep — a page cut before a second, in-memory filter would come up short and the total
/// would count rows the screen never shows.
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

    /// <summary>
    /// Narrows to the bans in force, searches the address, and answers the requested page with the
    /// total that matched.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<BanDto>>> HandleAsync(
        ListBansQuery query,
        CancellationToken cancellationToken)
    {
        var episodes = _dbContext.BanEpisodes
            .AsNoTracking()
            .Where(BanEpisode.InForceAt(_clock.UtcNow));

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            episodes = episodes.Where(episode =>
                EF.Functions.ILike(episode.IpAddress, pattern, LikePattern.EscapeCharacter));
        }

        var page = await episodes.ToPagedAsync(query, ListBansSorts.Map, cancellationToken);

        return Result<PagedResult<BanDto>>.Ok(page.Map(episode =>
        {
            return new BanDto(
                episode.Id,
                episode.IpAddress,
                episode.Reason,
                episode.Failures,
                episode.BannedAt,
                episode.ExpiresAt);
        }));
    }
}
