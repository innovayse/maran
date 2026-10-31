using Maran.Modules.Firewall.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Firewall.Queries.ListWhitelist;

/// <summary>
/// Holds <see cref="ListWhitelistQuery"/> to the panel's paging bounds (rules/security.md "Input"). The
/// list has no typed filter of its own to validate.
/// </summary>
public sealed class ListWhitelistQueryValidator : PagedQueryValidator<ListWhitelistQuery, WhitelistEntry>
{
    /// <summary>Declares the paging rules against <see cref="ListWhitelistSorts.Map"/>.</summary>
    public ListWhitelistQueryValidator()
        : base(ListWhitelistSorts.Map)
    {
    }
}
