using Maran.Modules.Apps.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Apps.Queries.ListApps;

/// <summary>
/// Reads one page of the apps the server has, is installing or removing, optionally searched and filtered.
/// Bound straight from the query string
/// (<c>GET /api/v1/apps?page=1&amp;search=php&amp;kind=runtime&amp;state=installed&amp;sort=-installedAt</c>);
/// <see cref="ListAppsQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the app's id or name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">One of <see cref="AppSortFields"/> either way, or <see cref="SortRequest.Default"/> for kind then id.</param>
/// <param name="Kind">Only apps of this kind, or <c>null</c> for every kind.</param>
/// <param name="State">Only apps in this state, or <c>null</c> for every state.</param>
public sealed record ListAppsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    AppKind? Kind = null,
    AppState? State = null) : IPagedQuery;
