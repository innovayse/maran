using Maran.Modules.Mail.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Queries.ListMailDomains;

/// <summary>The sort whitelist of the mail domain list: its fields, its default and its tie-breaker.</summary>
public static class ListMailDomainsSorts
{
    /// <summary>Oldest first by default, and by id when two domains share a sort value.</summary>
    public static readonly SortMap<MailDomain> Map = new SortMapBuilder<MailDomain>()
        .By(MailDomainSortFields.Name, domain => domain.Name)
        .By(MailDomainSortFields.CreatedAt, domain => domain.CreatedAt)
        .DefaultTo(MailDomainSortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(domain => domain.Id);
}
