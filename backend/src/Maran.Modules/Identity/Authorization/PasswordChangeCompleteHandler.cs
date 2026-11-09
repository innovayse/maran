using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Decides <see cref="PasswordChangeCompleteRequirement"/>: a caller whose token carries
/// <see cref="PanelClaimTypes.PasswordChangeRequired"/> may reach only the endpoints marked
/// <see cref="AllowDuringPasswordChangeAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision is read off the token, not out of the database</b>, so it costs no query per request.
/// The claim is written when the token is issued (<c>JwtAccessTokenIssuer</c>) from the login's own
/// <c>PasswordChangeRequired</c>. The bounded cost: after the password is changed, the token already in
/// the browser still carries the claim until it is re-issued, at most one access-token lifetime — and the
/// SPA refreshes the session straight after the change, which re-issues it without the claim. The other
/// direction cannot lag: a login created with an initial password has never held a token without the
/// claim, and a refresh re-reads the row, so it cannot launder the flag.
/// </para>
/// <para>
/// <b>An unauthenticated caller is not this requirement's business.</b> It succeeds for them and the
/// policy's own <c>RequireAuthenticatedUser</c> refuses, so a missing token still produces 401.
/// </para>
/// <para>
/// <b>Unmet, not granted, outside a request.</b> When the resource is not an <see cref="HttpContext"/> the
/// requirement is left unmet, the safe direction for a control whose whole job is to refuse.
/// </para>
/// </remarks>
public sealed class PasswordChangeCompleteHandler : AuthorizationHandler<PasswordChangeCompleteRequirement>
{
    /// <summary>Succeeds unless the caller must change their password and is asking for something else.</summary>
    /// <param name="context">The authorization context; its resource is the current request.</param>
    /// <param name="requirement">The requirement being evaluated.</param>
    /// <returns>Resolves once the requirement has been marked succeeded or left unmet.</returns>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PasswordChangeCompleteRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (!context.User.HasClaim(claim =>
            {
                return claim.Type == PanelClaimTypes.PasswordChangeRequired;
            }))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.Resource is HttpContext httpContext
            && httpContext.GetEndpoint()?.Metadata.GetMetadata<AllowDuringPasswordChangeAttribute>() is not null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
