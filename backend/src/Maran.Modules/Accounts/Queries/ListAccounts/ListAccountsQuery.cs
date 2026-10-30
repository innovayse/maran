using Maran.Modules.Accounts.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListAccounts;

/// <summary>
/// Reads one page of the hosting accounts, optionally searched and narrowed to a plan or a status.
/// Bound straight from the query string (<c>GET /api/v1/accounts?page=2&amp;search=olive</c>);
/// <see cref="ListAccountsQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// <paramref name="Status"/> is written on the query string in the camelCase the JSON answers carry
/// (<c>status=suspended</c>), so the SPA sends back exactly the spelling it reads; the binder parses
/// an enum name ignoring case, and a number that names no member is refused by the validator.
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the name or the primary domain, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">
/// One of <see cref="AccountSortFields"/> either way (<c>name</c>, <c>-createdAt</c>, …), or
/// <see cref="SortRequest.Default"/> for oldest first.
/// </param>
/// <param name="PlanId">Only accounts on this plan, or <c>null</c> for every plan.</param>
/// <param name="Status">Only accounts in this state, or <c>null</c> for every state.</param>
public sealed record ListAccountsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? PlanId = null,
    AccountStatus? Status = null) : IPagedQuery;
