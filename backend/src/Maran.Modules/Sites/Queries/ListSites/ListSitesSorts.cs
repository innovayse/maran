using System.Linq.Expressions;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Sites.Queries.ListSites;

/// <summary>
/// The orders the sites list accepts. <see cref="ListSitesQueryValidator"/> refuses any other sort and
/// <see cref="ListSitesQueryHandler"/> orders by exactly these, so the whitelist is written once.
/// </summary>
public static class ListSitesSorts
{
    /// <summary>
    /// A site's place in the status order: <see cref="SiteStatus.Enabled"/> is 0 and
    /// <see cref="SiteStatus.Disabled"/> is 1. A member added to <see cref="SiteStatus"/> without a rank
    /// here ranks 2 and fails <c>ListSitesSortsTests</c>, which requires one distinct rank per member.
    /// </summary>
    public static readonly Expression<Func<Site, int>> StatusRank = site =>
        site.Status == SiteStatus.Enabled ? 0 : site.Status == SiteStatus.Disabled ? 1 : 2;

    /// <summary>
    /// <see cref="SiteSortFields.Domain"/>, <see cref="SiteSortFields.CreatedAt"/> and
    /// <see cref="SiteSortFields.Status"/>, oldest first when none is named — the order the list had
    /// before it was paged — with ties broken by the site id.
    /// </summary>
    /// <remarks>
    /// The status column stores the member's NAME (<c>HasConversion&lt;string&gt;</c>), so ordering
    /// by the column itself would be alphabetical — an accident of English spelling, not a meaning.
    /// The status is therefore ordered by <see cref="StatusRank"/>: serving sites first, then
    /// suspended ones. EF Core translates the conditional into a <c>CASE</c>, so no migration and no
    /// second column are needed.
    /// </remarks>
    public static readonly SortMap<Site> Map = new SortMapBuilder<Site>()
        .By(SiteSortFields.Domain, site => site.Domain)
        .By(SiteSortFields.CreatedAt, site => site.CreatedAt)
        .By(SiteSortFields.Status, StatusRank)
        .DefaultTo(SiteSortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(site => site.Id);
}
