using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListResellerChoices;

/// <summary>
/// Reads one page of reseller names for a picker, so the owner select of the account form never loads
/// the whole reseller list. Bound straight from the query string
/// (<c>GET /api/v1/resellers/choices?search=ac</c>) and held to the same paging bounds as every list by
/// <see cref="ListResellerChoicesQueryValidator"/>, which is what caps it.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the reseller name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">
/// <see cref="ResellerChoiceSortFields.Name"/> either way, or <see cref="SortRequest.Default"/> for alphabetical.
/// </param>
public sealed record ListResellerChoicesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
