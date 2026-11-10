using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.ServerMigration.Queries.ListDiscoveredAccounts;

/// <summary>Holds <see cref="ListDiscoveredAccountsQuery"/> to the panel's paging bounds (rules/security.md "Input").</summary>
/// <remarks>
/// The <c>has</c> filter needs no rule of its own: it is an exact match against a closed set of words, so a spelling
/// that is none of them matches nothing, which is the honest answer to a thing no account can have.
/// </remarks>
public sealed class ListDiscoveredAccountsQueryValidator : PagedQueryValidator<ListDiscoveredAccountsQuery, DiscoveredAccount>
{
    /// <summary>Declares the paging rules against <see cref="ListDiscoveredAccountsSorts.Map"/>.</summary>
    public ListDiscoveredAccountsQueryValidator()
        : base(ListDiscoveredAccountsSorts.Map)
    {
    }
}
