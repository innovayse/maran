using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Databases.Queries.ListDatabaseUsers;

/// <summary>
/// Reads one page of the database users the caller may see, optionally searched and narrowed to one
/// account. Bound straight from the query string
/// (<c>GET /api/v1/database-users?page=2&amp;search=report&amp;sort=-createdAt&amp;accountId=…</c>).
/// </summary>
/// <remarks>
/// The tenant scope never comes from this query: the context's global query filter scopes the read to
/// the caller's own account. <paramref name="AccountId"/> only narrows that scope further and the two are
/// combined with AND, so a customer naming another account's id gets an empty page, never that account's rows.
/// </remarks>
/// <param name="Page">The 1-based page requested.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the user's full name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">One of <see cref="DatabaseUserSortFields"/> either way, or <see cref="SortRequest.Default"/>.</param>
/// <param name="AccountId">Only this account's users, or <c>null</c> for every account the caller may see.</param>
public sealed record ListDatabaseUsersQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? AccountId = null) : IPagedQuery;
