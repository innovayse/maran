using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Firewall.Queries.ListBans;

/// <summary>
/// Reads one page of the bans still in force, optionally searched. Bound straight from the query
/// string (<c>GET /api/v1/firewall/bans?page=2&amp;search=203.0&amp;sort=-expiresAt</c>);
/// <see cref="ListBansQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the banned address, or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="BanSortFields"/> either way (<c>address</c>, <c>-expiresAt</c>), or
/// <see cref="SortRequest.Default"/> for the newest ban first.
/// </param>
public sealed record ListBansQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
