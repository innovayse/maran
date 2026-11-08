using Maran.Modules.Mail.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Mail.Queries.ListMailDomains;

/// <summary>Holds the mail domain list to the paging bounds and the sort whitelist only the list knows.</summary>
public sealed class ListMailDomainsQueryValidator : PagedQueryValidator<ListMailDomainsQuery, MailDomain>
{
    /// <summary>Binds the list's sort whitelist to the shared paging rules.</summary>
    public ListMailDomainsQueryValidator()
        : base(ListMailDomainsSorts.Map)
    {
    }
}
