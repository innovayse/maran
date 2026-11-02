using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Queries.ListSubUsers;

/// <summary>Lists the people the caller added to their account, one page at a time.</summary>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">Rows per page, one of the sizes the panel offers.</param>
/// <param name="Search">Narrows the list to logins whose name or address contains the term.</param>
/// <param name="Sort">The sort asked for, or the default (oldest first).</param>
public sealed record ListSubUsersQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
