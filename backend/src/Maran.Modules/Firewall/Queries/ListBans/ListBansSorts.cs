using Maran.Modules.Firewall.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Firewall.Queries.ListBans;

/// <summary>
/// The orders the bans list accepts. <see cref="ListBansQueryValidator"/> refuses any other sort and
/// <see cref="ListBansQueryHandler"/> orders by exactly these, so the whitelist is written once.
/// </summary>
public static class ListBansSorts
{
    /// <summary>
    /// <see cref="BanSortFields.Address"/>, <see cref="BanSortFields.BannedAt"/> and
    /// <see cref="BanSortFields.ExpiresAt"/>, newest ban first when none is named — the order the list
    /// had before it was paged — with ties broken by the episode id.
    /// </summary>
    /// <remarks>
    /// The address orders as text, which is the order a reader scanning the column expects of the
    /// spelling they see; it is not numeric address order, and does not claim to be. A ban with no
    /// expiry sorts where PostgreSQL puts a null: after every timed one ascending, before them
    /// descending — "runs out last" either way round.
    /// </remarks>
    public static readonly SortMap<BanEpisode> Map = new SortMapBuilder<BanEpisode>()
        .By(BanSortFields.Address, episode => episode.IpAddress)
        .By(BanSortFields.BannedAt, episode => episode.BannedAt)
        .By(BanSortFields.ExpiresAt, episode => episode.ExpiresAt)
        .DefaultTo(BanSortFields.BannedAt, SortDirection.Descending)
        .TieBreaker(episode => episode.Id);
}
