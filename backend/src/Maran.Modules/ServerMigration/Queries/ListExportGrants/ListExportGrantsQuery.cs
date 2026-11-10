using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListExportGrants;

/// <summary>
/// Reads one page of the pull grants, optionally searched by the exported account's name and narrowed to one state.
/// Bound straight from the query string
/// (<c>GET /api/v1/account-exports/grants?search=alice&amp;state=Live&amp;sort=expiresAt</c>);
/// <see cref="ListExportGrantsQueryValidator"/> holds every member to its bounds.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the account's name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">One of <see cref="ExportGrantSortFields"/> either way, or <see cref="SortRequest.Default"/>.</param>
/// <param name="State">
/// Only grants in this state, spelled as the state's name (<c>Live</c>, <c>Used</c>, <c>Revoked</c> or
/// <c>Expired</c>), or <see cref="FilterValue.None"/> for every state. A spelling that names no state matches no grant.
/// </param>
public sealed record ListExportGrantsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    FilterValue State = default) : IPagedQuery;
