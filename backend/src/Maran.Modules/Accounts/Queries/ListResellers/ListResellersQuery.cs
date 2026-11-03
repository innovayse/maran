using Maran.Modules.Accounts.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListResellers;

/// <summary>
/// Reads one page of resellers, optionally searched and narrowed to a state. Bound straight from the
/// query string (<c>GET /api/v1/resellers?page=2&amp;search=acme</c>);
/// <see cref="ListResellersQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// <paramref name="Status"/> is written on the query string in the camelCase the JSON answers carry
/// (<c>status=suspended</c>), so the SPA sends back exactly the spelling it reads.
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the name or the contact address, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">
/// One of <see cref="ResellerSortFields"/> either way (<c>name</c>, <c>-createdAt</c>, …), or
/// <see cref="SortRequest.Default"/> for alphabetical by name.
/// </param>
/// <param name="Status">Only resellers in this state, or <c>null</c> for every state.</param>
public sealed record ListResellersQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    ResellerStatus? Status = null) : IPagedQuery;
