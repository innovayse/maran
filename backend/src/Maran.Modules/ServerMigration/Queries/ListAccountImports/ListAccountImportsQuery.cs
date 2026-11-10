using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListAccountImports;

/// <summary>
/// Reads one page of the imports, optionally searched by the account's name or the sending server's name and
/// narrowed to one state. Bound straight from the query string
/// (<c>GET /api/v1/account-imports?search=alice&amp;state=Running&amp;sort=-createdAt</c>);
/// <see cref="ListAccountImportsQueryValidator"/> holds every member to its bounds.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the account's name or the sending server's name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">One of <see cref="AccountImportSortFields"/> either way, or <see cref="SortRequest.Default"/>.</param>
/// <param name="State">
/// Only imports in this state, spelled as the state's name (<c>Running</c>), or <see cref="FilterValue.None"/> for
/// every state. A spelling that names no state matches no import.
/// </param>
public sealed record ListAccountImportsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    FilterValue State = default) : IPagedQuery;
