namespace Maran.Modules.Identity.Queries.GetSecurityPolicy;

/// <summary>
/// Reads the panel's security policy. It takes no parameters: there is exactly one policy on a
/// panel, and it is not scoped to an account.
/// </summary>
public sealed record GetSecurityPolicyQuery();
