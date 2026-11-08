namespace Maran.Modules.Accounts.Queries.GetMyBranding;

/// <summary>
/// Reads the branding the shell of the CALLER's account shows. Carries no account or reseller id: the
/// account is the one the caller's own token names.
/// </summary>
public sealed record GetMyBrandingQuery;
