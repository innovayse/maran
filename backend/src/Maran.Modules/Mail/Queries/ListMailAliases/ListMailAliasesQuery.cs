using Maran.Modules.Mail.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Queries.ListMailAliases;

/// <summary>
/// Reads one page of the aliases, forwarders and catch-alls the caller may see, optionally searched and
/// narrowed. Bound straight from the query string
/// (<c>GET /api/v1/mail-aliases?page=2&amp;search=info&amp;domain=example.com&amp;kind=forwarder</c>);
/// <see cref="ListMailAliasesQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// The tenant scope never comes from this query: the context's global query filter scopes the read to the
/// caller's own account (rules/security.md item 6). Every filter only narrows that scope further and is
/// combined with it by AND — a customer naming another account gets an empty page and a total of zero.
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the local part, the domain name or any target address, or
/// <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="MailAliasSortFields"/> either way (<c>address</c>, <c>-createdAt</c>), or
/// <see cref="SortRequest.Default"/> for oldest first.
/// </param>
/// <param name="AccountId">Only this account's addresses, or <c>null</c> for every account the caller may see.</param>
/// <param name="Domain">Only this domain, by NAME, or <see cref="FilterValue.None"/> for every domain.</param>
/// <param name="Kind">Only this kind, or <c>null</c> for every kind.</param>
public sealed record ListMailAliasesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? AccountId = null,
    FilterValue Domain = default,
    MailAliasKind? Kind = null) : IPagedQuery;
