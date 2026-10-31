using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Firewall.Queries.ListWhitelist;

/// <summary>
/// Reads one page of the address ranges the panel's automatic bans never touch, optionally searched.
/// Bound straight from the query string
/// (<c>GET /api/v1/firewall/whitelist?page=2&amp;search=office&amp;sort=cidr</c>);
/// <see cref="ListWhitelistQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the range or the note it was added under, or
/// <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="WhitelistEntrySortFields"/> either way (<c>cidr</c>, <c>-createdAt</c>), or
/// <see cref="SortRequest.Default"/> for the oldest row first.
/// </param>
public sealed record ListWhitelistQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
