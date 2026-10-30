using Maran.Modules.Accounts.Common;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListPlans;

/// <summary>
/// The orders the plans list accepts. <see cref="ListPlansQueryValidator"/> refuses any other sort and
/// <see cref="ListPlansQueryHandler"/> orders by exactly these, so the whitelist is written once.
/// </summary>
/// <remarks>
/// The map orders <see cref="PlanDto"/> rows, not <c>Plan</c> entities, because the name a plan is
/// sorted by is the one the caller reads: a shipped plan's name is a resource resolved in the request's
/// culture, which no column holds and no SQL <c>ORDER BY</c> can see. The handler therefore pages the
/// plans in memory (see its remarks for why that is bounded).
/// </remarks>
public static class ListPlansSorts
{
    /// <summary>
    /// <see cref="PlanSortFields.Name"/>, <see cref="PlanSortFields.DiskQuota"/> and
    /// <see cref="PlanSortFields.Accounts"/>, smallest disk quota first when none is named — the order
    /// the list had before it was paged — with ties broken by the plan id, so two plans selling the
    /// same disk never swap places between two reads of unchanged data.
    /// </summary>
    public static readonly SortMap<PlanDto> Map = new SortMapBuilder<PlanDto>()
        .By(PlanSortFields.Name, plan => plan.DisplayName)
        .By(PlanSortFields.DiskQuota, plan => plan.DiskQuotaMb)
        .By(PlanSortFields.Accounts, plan => plan.AccountCount)
        .DefaultTo(PlanSortFields.DiskQuota, SortDirection.Ascending)
        .TieBreaker(plan => plan.Id);
}
