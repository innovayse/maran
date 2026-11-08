using System.Diagnostics;
using Maran.Host.Authorization.Enums;
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
        AddScopePolicy(options, ApiKeyPolicyNames.SelfRead, ApiKeyScopes.SelfRead, KeyOwner.Anyone);
        AddScopePolicy(options, ApiKeyPolicyNames.ServerRead, ApiKeyScopes.ServerRead, KeyOwner.Administrator);
        AddScopePolicy(options, ApiKeyPolicyNames.PlansRead, ApiKeyScopes.PlansRead, KeyOwner.Reseller);
        AddScopePolicy(options, ApiKeyPolicyNames.AccountsRead, ApiKeyScopes.AccountsRead, KeyOwner.Reseller);
        AddScopePolicy(options, ApiKeyPolicyNames.AccountsWrite, ApiKeyScopes.AccountsWrite, KeyOwner.Reseller);
        AddScopePolicy(options, ApiKeyPolicyNames.AccountsDelete, ApiKeyScopes.AccountsDelete, KeyOwner.Reseller);
    }

    /// <summary>
    /// Registers one policy: the <c>ApiKey</c> scheme, a key principal, one scope, and who must own the key.
    /// </summary>
    /// <param name="options">The authorization options being configured.</param>
    /// <param name="policyName">The policy's name.</param>
    /// <param name="scope">The scope the key must hold.</param>
    /// <param name="owner">Whose key may pass: a reseller's (its owner claim must be present), the administrator's (it must be absent) or either's.</param>
    private static void AddScopePolicy(AuthorizationOptions options, string policyName, string scope, KeyOwner owner)
    {
        options.AddPolicy(policyName, policy =>
        {
            policy.AddAuthenticationSchemes(ApiKeyDefaults.Scheme);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(PanelClaimTypes.ApiKeyId);
            policy.RequireClaim(PanelClaimTypes.ApiKeyScope, scope);

            // The scope vocabulary is split by owner and the entity enforces it at creation; this is the
            // second lock on the same door, so a scope that reached the wrong kind of key by any path
            // still does not open an endpoint of the other kind.
            switch (owner)
            {
                case KeyOwner.Reseller:
                    policy.RequireClaim(PanelClaimTypes.ApiKeyReseller);
                    break;
                case KeyOwner.Administrator:
                    policy.RequireAssertion(context =>
                    {
                        return !context.User.HasClaim(claim => { return claim.Type == PanelClaimTypes.ApiKeyReseller; });
                    });
                    break;
                case KeyOwner.Anyone:
                    break;
                default:
                    throw new UnreachableException("KeyOwner has three members.");
            }
        });
    }
}
