namespace Maran.Modules.Accounts.Queries.GetResellerBranding;

/// <summary>
/// Reads the branding of the reseller the CALLER is. Carries no reseller id: the answer is always the
/// one the caller's own token names.
/// </summary>
public sealed record GetResellerBrandingQuery;
