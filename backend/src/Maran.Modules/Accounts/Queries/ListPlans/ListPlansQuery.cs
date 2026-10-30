using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListPlans;

/// <summary>
/// Reads one page of the plans a server sells, optionally searched by name. Bound straight from the
/// query string (<c>GET /api/v1/plans?page=2&amp;search=agency&amp;sort=-accounts</c>);
/// <see cref="ListPlansQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally, ignoring case, inside the name the caller reads — the translated name of
/// a plan the panel ships, the operator's own text for one an operator named — or
/// <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="PlanSortFields"/> either way (<c>name</c>, <c>-accounts</c>, …), or
/// <see cref="SortRequest.Default"/> for the smallest disk quota first.
/// </param>
public sealed record ListPlansQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
