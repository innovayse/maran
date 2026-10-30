namespace Maran.Modules.Accounts.Queries.GetPlan;

/// <summary>
/// Reads one plan with its limits and how many accounts are on it — what the plan edit screen opens
/// with, so it never has to page through the plan list to find the plan it was linked to.
/// </summary>
/// <param name="PlanId">The plan to read, from the route.</param>
public sealed record GetPlanQuery(Guid PlanId);
