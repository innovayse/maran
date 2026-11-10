using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListDiscoveredAccounts;

/// <summary>
/// The orders the discovered accounts list accepts. <see cref="ListDiscoveredAccountsQueryValidator"/> refuses any
/// other sort and <see cref="ListDiscoveredAccountsQueryHandler"/> orders by exactly these.
/// </summary>
public static class ListDiscoveredAccountsSorts
{
    /// <summary>
    /// <see cref="DiscoveredAccountSortFields.Name"/>, <see cref="DiscoveredAccountSortFields.Disk"/>,
    /// <see cref="DiscoveredAccountSortFields.Sites"/> and <see cref="DiscoveredAccountSortFields.Databases"/>, by
    /// name when none is named, with ties broken by the row id.
    /// </summary>
    /// <remarks>
    /// <c>disk</c> orders by the measured bytes; an account whose home could not be measured stores zero and sorts
    /// with the smallest, which the row's <c>diskKnown</c> flag tells the reader apart from a real zero.
    /// </remarks>
    public static readonly SortMap<DiscoveredAccount> Map = new SortMapBuilder<DiscoveredAccount>()
        .By(DiscoveredAccountSortFields.Name, account => account.Name)
        .By(DiscoveredAccountSortFields.Disk, account => account.DiskBytes)
        .By(DiscoveredAccountSortFields.Sites, account => account.SiteCount)
        .By(DiscoveredAccountSortFields.Databases, account => account.DatabaseCount)
        .DefaultTo(DiscoveredAccountSortFields.Name, SortDirection.Ascending)
        .TieBreaker(account => account.Id);
}
