using Maran.Modules.Accounts.Common;
using Maran.Sdk.Validators;

namespace Maran.Modules.Accounts.Queries.ListPlans;

/// <summary>
/// Holds <see cref="ListPlansQuery"/> to the panel's paging bounds (rules/security.md "Input"); the
/// list has no typed filter of its own.
/// </summary>
public sealed class ListPlansQueryValidator : PagedQueryValidator<ListPlansQuery, PlanDto>
{
    /// <summary>Declares the paging rules against <see cref="ListPlansSorts.Map"/>.</summary>
    public ListPlansQueryValidator()
        : base(ListPlansSorts.Map)
    {
    }
}
