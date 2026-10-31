using Maran.Modules.Firewall.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Firewall.Queries.ListWhitelist;

/// <summary>
/// The orders the whitelist accepts. <see cref="ListWhitelistQueryValidator"/> refuses any other sort
/// and <see cref="ListWhitelistQueryHandler"/> orders by exactly these, so the whitelist of sorts is
/// written once.
/// </summary>
public static class ListWhitelistSorts
{
    /// <summary>
    /// <see cref="WhitelistEntrySortFields.Cidr"/> and <see cref="WhitelistEntrySortFields.CreatedAt"/>,
    /// oldest first when none is named — the order the list had before it was paged, which puts the
    /// installer's seeded row first, since an operator reading this screen is asking "why am I exempt"
    /// before "who else is" — with ties broken by the row id.
    /// </summary>
    /// <remarks>The range orders as text, the spelling the column shows, not as numeric address order.</remarks>
    public static readonly SortMap<WhitelistEntry> Map = new SortMapBuilder<WhitelistEntry>()
        .By(WhitelistEntrySortFields.Cidr, entry => entry.Cidr)
        .By(WhitelistEntrySortFields.CreatedAt, entry => entry.CreatedAt)
        .DefaultTo(WhitelistEntrySortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(entry => entry.Id);
}
