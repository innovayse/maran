using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Decides <see cref="TwoFactorEnrolmentCompleteRequirement"/>: a caller whose token carries
/// <see cref="PanelClaimTypes.TwoFactorSetupRequired"/> may reach only the endpoints marked
/// <see cref="AllowDuringTwoFactorEnrolmentAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision is read off the token, not out of the database.</b> The claim is written when the
/// token is issued (<c>JwtAccessTokenIssuer</c>), so this runs on every authorized request without a
/// query. The cost of that choice is bounded and stated: a policy change takes effect for an
/// existing session when its access token is next re-issued, which is at most one access-token
/// lifetime away — fifteen minutes — because the refresh endpoint re-evaluates the policy for the
/// same user. Turning the steering ON is therefore delayed by up to a token lifetime, and turning it
/// OFF likewise; neither direction leaves a caller with more access than the policy in force before
/// the change allowed.
/// </para>
/// <para>
/// <b>An unauthenticated caller is not this requirement's business.</b> It succeeds for them, and
/// the policy's own <c>RequireAuthenticatedUser</c> is what refuses — so a missing token produces
/// 401 as it always did, rather than a 403 that would say "you are signed in but steered" to
/// somebody who is not signed in at all.
/// </para>
/// </remarks>
public sealed class TwoFactorEnrolmentCompleteHandler : AuthorizationHandler<TwoFactorEnrolmentCompleteRequirement>
{
    /// <summary>Succeeds unless the caller is steered into enrolment and is asking for something else.</summary>
    /// <param name="context">The authorization context; its resource is the current request.</param>
    /// <param name="requirement">The requirement being evaluated.</param>
    /// <returns>Resolves once the requirement has been marked succeeded or left unmet.</returns>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TwoFactorEnrolmentCompleteRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (!context.User.HasClaim(claim =>
            {
                return claim.Type == PanelClaimTypes.TwoFactorSetupRequired;
            }))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // With endpoint routing the resource IS the HttpContext, which is how the endpoint's own
        // metadata becomes readable here. When it is not — an authorization check made outside the
        // request pipeline — the requirement is left unmet rather than granted: the safe direction
        // for a control whose whole job is to refuse.
        if (context.Resource is HttpContext httpContext
            && httpContext.GetEndpoint()?.Metadata.GetMetadata<AllowDuringTwoFactorEnrolmentAttribute>() is not null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
