using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListAccountExports;

/// <summary>
/// Reads one page of the exports an administrator made, optionally searched by the exported account's name. Bound
/// straight from the query string (<c>GET /api/v1/account-exports?search=alice&amp;sort=-expiresAt</c>);
/// <see cref="ListAccountExportsQueryValidator"/> holds every member to its bounds.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the account's name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">One of <see cref="AccountExportSortFields"/> either way, or <see cref="SortRequest.Default"/>.</param>
public sealed record ListAccountExportsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
