using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Host.Authorization;

/// <summary>
/// The authorization policies that admit an API key: one per scope, each requiring the <c>ApiKey</c>
/// authentication scheme and the scope's claim. Their names are <see cref="ApiKeyPolicyNames"/>, so a
/// module can declare them on an endpoint without referencing the Host.
/// </summary>
/// <remarks>
/// These are the only policies that name the <c>ApiKey</c> scheme. Every policy in
/// <see cref="RolePolicies"/>, the fallback and the default name no scheme, so they authenticate with
/// the default JWT scheme and a key never satisfies them. Registering these changes neither the
/// default scheme, the fallback policy nor the default policy.
/// </remarks>
public static class ApiKeyPolicies
{
    /// <summary>
    /// Registers one policy per scope. A key principal is recognised by its key id claim, which only
    /// the API key handler writes, so a JWT that merely carries a scope-named claim cannot pass.
    /// </summary>
    /// <param name="options">The authorization options being configured.</param>
    public static void Configure(AuthorizationOptions options)
    {
        AddScopePolicy(options, ApiKeyPolicyNames.SelfRead, ApiKeyScopes.SelfRead);
        AddScopePolicy(options, ApiKeyPolicyNames.ServerRead, ApiKeyScopes.ServerRead);
    }

    /// <summary>Registers one policy: the <c>ApiKey</c> scheme, a key principal, and one scope.</summary>
    /// <param name="options">The authorization options being configured.</param>
    /// <param name="policyName">The policy's name.</param>
    /// <param name="scope">The scope the key must hold.</param>
    private static void AddScopePolicy(AuthorizationOptions options, string policyName, string scope)
    {
        options.AddPolicy(policyName, policy =>
        {
            policy.AddAuthenticationSchemes(ApiKeyDefaults.Scheme);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(PanelClaimTypes.ApiKeyId);
            policy.RequireClaim(PanelClaimTypes.ApiKeyScope, scope);
        });
    }
}
