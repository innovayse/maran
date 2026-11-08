namespace Maran.Modules.Accounts.Queries.GetAccountUsage;

/// <summary>
/// Reads one account's use for an integration: state, plan, disk occupied against the plan's quota, and
/// the resource counts against the plan's limits.
/// </summary>
/// <param name="AccountId">The account to read, from the route.</param>
public sealed record GetAccountUsageQuery(Guid AccountId);
