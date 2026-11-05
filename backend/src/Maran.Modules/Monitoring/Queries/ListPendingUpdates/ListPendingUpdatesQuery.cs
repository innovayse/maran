using Maran.Modules.Monitoring.Domain.Policies;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Monitoring.Queries.ListPendingUpdates;

/// <summary>
/// Reads one page of the package updates the server's package manager already knows are pending,
/// optionally searched by package name. Bound straight from the query string
/// (<c>GET /api/v1/monitoring/updates?page=2&amp;search=ssl&amp;sort=-source</c>);
/// <see cref="ListPendingUpdatesQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// The list is host-wide by nature and the route is administrators only; there is no account id to
/// pass. The agent answers the whole list in one call and the panel pages it in memory, because the
/// agent has no way to page a package manager's answer.
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally, ignoring case, inside the package name, or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="PendingUpdateSortFields"/> either way (<c>packageName</c>, <c>-source</c>,
/// <c>security</c>), or <see cref="SortRequest.Default"/> for package name ascending.
/// </param>
/// <param name="Classification">
/// Only updates carrying this word (<c>security</c>, <c>ordinary</c> or <c>unmeasured</c>, see
/// <see cref="PendingUpdateClassificationPolicy"/>), or <see cref="FilterValue.None"/> for every
/// update. Applied in the panel, before the search and the page, so the total is the filtered total.
/// </param>
public sealed record ListPendingUpdatesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    FilterValue Classification = default) : IPagedQuery;
