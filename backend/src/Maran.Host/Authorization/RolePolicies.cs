using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Host.Authorization;

/// <summary>
/// The panel's authorization policies. Two are enough for v1's role model (spec §8): an
/// administrator reaches the whole server, a customer reaches their own account.
/// </summary>
public static class RolePolicies
{
    /// <summary>Requires an authenticated administrator.</summary>
    public const string AdminOnly = AuthorizationPolicies.AdminOnly;

    /// <summary>Requires only that the caller is signed in.</summary>
    public const string AnyAuthenticated = AuthorizationPolicies.AnyAuthenticated;

    /// <summary>Registers both policies and makes the stricter default apply to unmarked endpoints.</summary>
    /// <param name="options">The authorization options being configured.</param>
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(AnyAuthenticated, policy =>
        {
            policy.RequireAuthenticatedUser();
        });

        options.AddPolicy(AdminOnly, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(PanelClaimTypes.Role, nameof(UserRole.Admin));
        });

        // An endpoint that forgets its attribute then denies rather than opens. The failure mode of
        // a missing rule must be a refusal: a forgotten [Authorize] is invisible in review and in
        // testing, while a forgotten [AllowAnonymous] fails loudly the first time anyone calls it.
        options.FallbackPolicy = options.GetPolicy(AnyAuthenticated);
    }
}
