using Maran.Modules.Accounts.Common;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListPlanChoices;

/// <summary>The orders the plan picker accepts, read by its validator and its handler alike.</summary>
public static class ListPlanChoicesSorts
{
    /// <summary>
    /// <see cref="PlanChoiceSortFields.Name"/> only, alphabetical by default — a picker is scanned by
    /// name — ties broken by id. Over <see cref="PlanChoiceDto"/> rows because the name is the one the
    /// caller reads, resolved in their culture (see <see cref="ListPlanChoicesQueryHandler"/>).
    /// </summary>
    public static readonly SortMap<PlanChoiceDto> Map = new SortMapBuilder<PlanChoiceDto>()
        .By(PlanChoiceSortFields.Name, plan => plan.DisplayName)
        .DefaultTo(PlanChoiceSortFields.Name, SortDirection.Ascending)
        .TieBreaker(plan => plan.Id);
}
