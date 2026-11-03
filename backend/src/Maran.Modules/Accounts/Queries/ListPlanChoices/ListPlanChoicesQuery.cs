using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListPlanChoices;

/// <summary>
/// Reads one page of plan names for a picker, so a screen that only needs to choose a plan never
/// loads the plan list with its headcounts. Bound straight from the query string
/// (<c>GET /api/v1/plans/choices?search=agen</c>) and held to the same paging bounds as every list by
/// <see cref="ListPlanChoicesQueryValidator"/>, which is what caps it.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally, ignoring case, inside the name the caller reads, or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// <see cref="PlanChoiceSortFields.Name"/> either way, or <see cref="SortRequest.Default"/> for alphabetical.
/// </param>
/// <param name="ForResellerId">
/// The reseller an account is being created or moved for: only the plans that reseller owns are offered,
/// because an account is sold only from its owner's plans. <c>null</c> offers the administrator's own
/// plans. A reseller caller is always offered its own plans, and naming another reseller is answered
/// <c>ResellerNotFound</c>, the same as for one that does not exist.
/// </param>
public sealed record ListPlanChoicesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? ForResellerId = null) : IPagedQuery;
