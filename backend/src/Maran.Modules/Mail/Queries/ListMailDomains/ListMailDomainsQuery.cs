using Maran.Modules.Mail.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Queries.ListMailDomains;

/// <summary>
/// Lists mail domains a page at a time. Whose domains are returned is decided by the context's tenant
/// filter: a customer sees their own account's, an administrator everyone's.
/// </summary>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">The page size; one of the allowed sizes.</param>
/// <param name="Search">A term matched literally and case-insensitively inside the domain name, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">The sort request, or the default (oldest first).</param>
/// <param name="AccountId">Only domains of this account, or <c>null</c> for every account the caller sees.</param>
/// <param name="Status">Only domains read as this status, or <c>null</c> for every status.</param>
public sealed record ListMailDomainsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? AccountId = null,
    MailDomainStatus? Status = null) : IPagedQuery;
