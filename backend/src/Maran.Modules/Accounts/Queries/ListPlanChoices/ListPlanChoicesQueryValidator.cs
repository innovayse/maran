using Maran.Modules.Accounts.Common;
using Maran.Sdk.Validators;

namespace Maran.Modules.Accounts.Queries.ListPlanChoices;

/// <summary>
/// Holds <see cref="ListPlanChoicesQuery"/> to the panel's paging bounds; the page-size bound is what
/// keeps a picker from asking for every plan at once.
/// </summary>
public sealed class ListPlanChoicesQueryValidator : PagedQueryValidator<ListPlanChoicesQuery, PlanChoiceDto>
{
    /// <summary>Declares the paging rules against <see cref="ListPlanChoicesSorts.Map"/>.</summary>
    public ListPlanChoicesQueryValidator()
        : base(ListPlanChoicesSorts.Map)
    {
    }
}
