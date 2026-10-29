namespace Maran.Modules.Accounts.Commands.DeletePlan;

/// <summary>Deletes a plan no account is on.</summary>
/// <param name="PlanId">The plan to delete.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeletePlanCommand(Guid PlanId, string IpAddress, string UserAgent);
