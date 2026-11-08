namespace Maran.Sdk.Contracts;

/// <summary>
/// Names of the authorization policies that admit an API key, so a module can declare on an endpoint
/// that a key holding one scope may reach it.
/// </summary>
/// <remarks>
/// The same arrangement as <see cref="AuthorizationPolicies"/>: the Host defines what each policy
/// requires (the <c>ApiKey</c> authentication scheme and one scope claim), a module only says which
/// one applies, and a module never references the Host. A key reaches an endpoint ONLY when the
/// endpoint names one of these policies together with the <c>ApiKey</c> scheme; every other policy
/// authenticates with the default scheme and a key never satisfies it.
/// </remarks>
public static class ApiKeyPolicyNames
{
    /// <summary>Requires a valid API key; every valid key holds the scope this policy checks.</summary>
    public const string SelfRead = "ApiKeySelfRead";

    /// <summary>Requires a valid API key granted the read-only server scope.</summary>
    public const string ServerRead = "ApiKeyServerRead";

    /// <summary>Requires a valid API key owned by a reseller and granted the plans read scope.</summary>
    public const string PlansRead = "ApiKeyPlansRead";

    /// <summary>Requires a valid API key owned by a reseller and granted the accounts read scope.</summary>
    public const string AccountsRead = "ApiKeyAccountsRead";

    /// <summary>Requires a valid API key owned by a reseller and granted the accounts write scope.</summary>
    public const string AccountsWrite = "ApiKeyAccountsWrite";

    /// <summary>Requires a valid API key owned by a reseller and granted the accounts delete scope.</summary>
    public const string AccountsDelete = "ApiKeyAccountsDelete";
}
