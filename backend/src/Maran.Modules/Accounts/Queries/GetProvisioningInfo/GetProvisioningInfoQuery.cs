namespace Maran.Modules.Accounts.Queries.GetProvisioningInfo;

/// <summary>
/// Reads what the caller's reseller may provision. Carries no reseller id: the answer is always about
/// the reseller the caller's own identity names, so there is no parameter to ask for another.
/// </summary>
public sealed record GetProvisioningInfoQuery;
