using Maran.Modules.Identity.Authorization;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Host.Authorization;

/// <summary>
/// The panel's authorization policies. An administrator reaches the whole server, a reseller the
/// accounts and plans it sells, a customer their own account; four policies name the combinations an
/// endpoint can ask for.
/// </summary>
public static class RolePolicies
{
    /// <summary>
    /// Requires a signed-in administrator who has completed any forced two-factor enrolment. Also the
    /// fallback: an endpoint that carries no authorization metadata at all is administrator-only.
    /// </summary>
    public const string AdminOnly = AuthorizationPolicies.AdminOnly;

    /// <summary>
    /// Requires a signed-in reseller who has completed any forced two-factor enrolment. An
    /// administrator does not pass it, and neither does a customer.
    /// </summary>
    public const string ResellerOnly = AuthorizationPolicies.ResellerOnly;

    /// <summary>
    /// Requires a signed-in administrator or reseller who has completed any forced two-factor
    /// enrolment; a customer does not pass it. Passing it is never the whole of a reseller's scoping:
    /// the Accounts module narrows a reseller to what it owns.
    /// </summary>
    public const string AdminOrReseller = AuthorizationPolicies.AdminOrReseller;

    /// <summary>
    /// Requires a signed-in caller of any role who has completed any forced two-factor enrolment —
    /// a customer passes it. Also the default policy a bare <c>[Authorize]</c> evaluates.
    /// </summary>
    public const string AnyAuthenticated = AuthorizationPolicies.AnyAuthenticated;

    /// <summary>
    /// Registers the four policies, makes <see cref="AdminOnly"/> the fallback for an endpoint that carries
    /// no authorization metadata, and makes <see cref="AnyAuthenticated"/> the default for a bare
    /// <c>[Authorize]</c>.
    /// </summary>
    /// <param name="options">The authorization options being configured.</param>
    public static void Configure(AuthorizationOptions options)
    {
        // Every policy below carries BOTH session gates: the forced two-factor enrolment and the
        // first-password change. The second is a property of the session, not of a resource, so it is
        // attached to all four; PasswordChangeGateTests (architecture suite) fails when a policy lacks it.
        options.AddPolicy(AnyAuthenticated, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new TwoFactorEnrolmentCompleteRequirement());
            policy.AddRequirements(new PasswordChangeCompleteRequirement());
            policy.AddRequirements(new ViewAsBoundaryRequirement());

            // The central membership guard (issues #331, #340): a customer token speaking for an account
            // its membership was suspended from, or was removed from, is refused on every endpoint
            // carrying this policy, not only at sign-in. Administrators and view-as tokens are exempt
            // (the handler's remarks); AdminOnly therefore does not carry it.
            policy.AddRequirements(new AccountMembershipActiveRequirement());
        });

        options.AddPolicy(AdminOnly, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(PanelClaimTypes.Role, nameof(UserRole.Admin));
            policy.AddRequirements(new TwoFactorEnrolmentCompleteRequirement());
            policy.AddRequirements(new PasswordChangeCompleteRequirement());
            policy.AddRequirements(new ViewAsBoundaryRequirement());
        });

        options.AddPolicy(ResellerOnly, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(PanelClaimTypes.Role, nameof(UserRole.Reseller));
            policy.AddRequirements(new TwoFactorEnrolmentCompleteRequirement());
            policy.AddRequirements(new PasswordChangeCompleteRequirement());
            policy.AddRequirements(new ViewAsBoundaryRequirement());
        });

        // Exactly the two role names and no third: a customer, or a role this build has never heard
        // of, passes neither value of the claim. A view-as token carries the VIEWED login's role, so an
        // administrator viewing as a reseller passes this policy as the reseller and passes AdminOnly
        // not at all.
        options.AddPolicy(AdminOrReseller, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(PanelClaimTypes.Role, nameof(UserRole.Admin), nameof(UserRole.Reseller));
            policy.AddRequirements(new TwoFactorEnrolmentCompleteRequirement());
            policy.AddRequirements(new PasswordChangeCompleteRequirement());
            policy.AddRequirements(new ViewAsBoundaryRequirement());
        });

        // The FALLBACK is what the framework evaluates for a request whose endpoint carries no
        // authorization metadata at all: no [Authorize], no [AllowAnonymous], no RequireAuthorization.
        // It is the administrator policy, so an endpoint that forgets its attribute refuses an
        // anonymous caller (401) and a signed-in customer (403), and answers an administrator only.
        // Administrator rather than nobody because an administrator already holds every power the
        // panel grants: an unmarked endpoint opened to them grants nothing new.
        //
        // The failure mode of a missing rule must be a refusal. A forgotten [Authorize] is invisible
        // in review and in testing, while a forgotten [AllowAnonymous] or AnyAuthenticated fails
        // loudly the first time a customer calls the endpoint.
        //
        // This is the running server's line for code this repository never tested, such as a
        // marketplace module's controller. The panel's own endpoints do not rely on it: each names
        // its policy or is marked anonymous, which EndpointAuthorizationPolicyTests holds. The
        // fallback also covers a request that matched no endpoint, so a customer gets 403 there and
        // an administrator 404 (AuthorizationFallbackEndpointTests). The history is in
        // docs/superpowers/notes/2026-10-26-authorization-fallback-threat-note.md.
        options.FallbackPolicy = options.GetPolicy(AdminOnly);

        // The DEFAULT policy is what a bare [Authorize] or RequireAuthorization() evaluates, and it is
        // deliberately NOT the fallback: it stays customer-level, so a bare [Authorize] means "any
        // signed-in user who has finished enrolment". The panel's own endpoints no longer use the bare
        // form — each names AnyAuthenticated — but a module that writes it still gets the two-factor
        // steering. Without this line the framework's default is a plain "is authenticated" carrying
        // none of the requirements above, and the forced-two-factor steering would be absent from
        // exactly the endpoints that opted into authorization by hand.
        options.DefaultPolicy = options.GetPolicy(AnyAuthenticated)!;
    }
}
