using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListAccountChoices;

/// <summary>
/// Reads one page of account names for a picker or an owner-name lookup, so a screen that only
/// needs to name an account never loads the whole account list. Bound straight from the query
/// string (<c>GET /api/v1/accounts/choices?search=ol</c>) and held to the same paging bounds as
/// every list by <see cref="ListAccountChoicesQueryValidator"/>, which is what caps it.
/// </summary>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the account name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">
/// <see cref="AccountChoiceSortFields.Name"/> either way, or <see cref="SortRequest.Default"/> for alphabetical.
/// </param>
public sealed record ListAccountChoicesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
