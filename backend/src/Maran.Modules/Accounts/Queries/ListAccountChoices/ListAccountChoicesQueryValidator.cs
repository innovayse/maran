using Maran.Modules.Accounts.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Accounts.Queries.ListAccountChoices;

/// <summary>
/// Holds <see cref="ListAccountChoicesQuery"/> to the panel's paging bounds; the page-size bound is
/// what keeps a picker from asking for every account at once.
/// </summary>
public sealed class ListAccountChoicesQueryValidator : PagedQueryValidator<ListAccountChoicesQuery, Account>
{
    /// <summary>Declares the paging rules against <see cref="ListAccountChoicesSorts.Map"/>.</summary>
    public ListAccountChoicesQueryValidator()
        : base(ListAccountChoicesSorts.Map)
    {
    }
}
