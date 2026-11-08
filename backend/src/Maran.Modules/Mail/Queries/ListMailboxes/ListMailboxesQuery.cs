using Maran.Modules.Mail.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Queries.ListMailboxes;

/// <summary>
/// Reads one page of the mailboxes the caller may see, optionally searched and narrowed by account,
/// domain and status. Bound straight from the query string
/// (<c>GET /api/v1/mailboxes?page=2&amp;search=info&amp;sort=-quotaMib&amp;domain=example.com&amp;status=disabled</c>);
/// <see cref="ListMailboxesQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// The tenant scope never comes from this query: the context's global query filter scopes the read to the
/// caller's own account (rules/security.md item 6). The filters only narrow that scope further and are
/// combined with AND, so a customer naming another account gets an empty page, never that account's rows.
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the local part or the domain name, or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">One of <see cref="MailboxSortFields"/> either way, or <see cref="SortRequest.Default"/>.</param>
/// <param name="AccountId">Only this account's mailboxes, or <c>null</c> for every account the caller may see.</param>
/// <param name="Domain">
/// Only the mailboxes of the domain with exactly this NAME (<c>example.com</c>, not an identifier), or
/// <see cref="FilterValue.None"/> for every domain.
/// </param>
/// <param name="Status">
/// Only mailboxes with this status, or <c>null</c> for every status. <c>suspended</c> is "the account is
/// suspended"; <c>disabled</c> is "switched off by the owner while the account is not suspended"; <c>active</c>
/// is everything else.
/// </param>
public sealed record ListMailboxesQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? AccountId = null,
    FilterValue Domain = default,
    MailboxStatus? Status = null) : IPagedQuery;
