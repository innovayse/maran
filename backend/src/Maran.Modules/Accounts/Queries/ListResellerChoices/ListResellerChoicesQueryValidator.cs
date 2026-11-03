using Maran.Modules.Accounts.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Accounts.Queries.ListResellerChoices;

/// <summary>
/// Holds <see cref="ListResellerChoicesQuery"/> to the panel's paging bounds; the page-size bound is
/// what keeps a picker from asking for every reseller at once.
/// </summary>
public sealed class ListResellerChoicesQueryValidator : PagedQueryValidator<ListResellerChoicesQuery, Reseller>
{
    /// <summary>Declares the paging rules against <see cref="ListResellerChoicesSorts.Map"/>.</summary>
    public ListResellerChoicesQueryValidator()
        : base(ListResellerChoicesSorts.Map)
    {
    }
}
