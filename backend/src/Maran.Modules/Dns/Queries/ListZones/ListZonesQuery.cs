using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Dns.Queries.ListZones;

/// <summary>
/// Reads one page of the zones the caller may see, optionally searched and narrowed to one account. Bound
/// straight from the query string
/// (<c>GET /api/v1/dns-zones?page=2&amp;search=example&amp;sort=-createdAt&amp;accountId=…</c>);
/// <see cref="ListZonesQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// The tenant scope never comes from this query: the context's global query filter scopes the read to the
/// caller's own account (rules/security.md item 6). <paramref name="AccountId"/> only narrows that scope
/// further and is combined with it by AND, so a customer naming another account's id gets an empty page.
/// </remarks>
/// <param name="Page">The 1-based page requested.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the zone's name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">One of <see cref="ZoneSortFields"/> either way, or <see cref="SortRequest.Default"/>.</param>
/// <param name="AccountId">Only this account's zones, or <c>null</c> for every account the caller may see.</param>
public sealed record ListZonesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? AccountId = null) : IPagedQuery;
