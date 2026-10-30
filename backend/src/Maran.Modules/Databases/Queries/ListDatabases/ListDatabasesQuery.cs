using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Databases.Queries.ListDatabases;

/// <summary>
/// Reads one page of the databases the caller may see, optionally searched and narrowed to one
/// account. Bound straight from the query string
/// (<c>GET /api/v1/databases?page=2&amp;search=shop&amp;sort=-createdAt&amp;accountId=…</c>);
/// <see cref="ListDatabasesQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// The tenant scope never comes from this query: the context's global query filter scopes the read to
/// the caller's own account (rules/security.md item 6). <paramref name="AccountId"/> only narrows that
/// scope further, and the two are combined with AND — a customer naming another account's id gets an
/// empty page with a total of zero, never that account's rows.
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the database's full name — the prefixed name MySQL holds and the
/// list shows — or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="DatabaseSortFields"/> either way (<c>name</c>, <c>-createdAt</c>), or
/// <see cref="SortRequest.Default"/> for oldest first.
/// </param>
/// <param name="AccountId">
/// Only this account's databases, or <c>null</c> for every account the caller may see. Bound from the
/// <c>accountId</c> query-string parameter.
/// </param>
public sealed record ListDatabasesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? AccountId = null) : IPagedQuery;
