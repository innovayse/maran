namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// One seeded reseller of <see cref="ResellerWorld"/>: the reseller row, the login that signs in for
/// it, the one plan it owns and the one account it owns on that plan.
/// </summary>
/// <param name="ResellerId">The reseller's id.</param>
/// <param name="UserId">The id of the login that acts for the reseller.</param>
/// <param name="Name">The reseller's name, which is also its login name.</param>
/// <param name="PlanId">The id of the plan the reseller owns.</param>
/// <param name="PlanName">The plan's name.</param>
/// <param name="AccountId">The id of the account the reseller owns, which is on <paramref name="PlanId"/>.</param>
/// <param name="AccountName">The account's name.</param>
public sealed record ResellerSeed(
    Guid ResellerId,
    Guid UserId,
    string Name,
    Guid PlanId,
    string PlanName,
    Guid AccountId,
    string AccountName);
