using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Maran.Host.Security;

/// <summary>
/// The bearer handler's last word on a view-as token: it is accepted only while the view-as session
/// behind it, and the operator's own sign-in, are both live (issue #317).
/// </summary>
/// <remarks>
/// <para>
/// <b>Here, in authentication, and not in an authorization requirement.</b> A refusal here is a 401,
/// which the SPA answers the way it answers any expired token — by refreshing with the operator's own
/// refresh cookie, which this feature never touches. So Back, an expiry and a revocation all land the
/// operator on their own session. A requirement would answer 403, and an SPA holding a token it can
/// neither use nor refresh is the stranded operator this design exists to prevent.
/// </para>
/// <para>
/// <b>It fails closed.</b> A token carrying either view-as marker but no readable actor, no readable
/// <c>sid</c> or <c>sub</c>, or a session the verifier does not confirm, fails authentication. An
/// exception from the database propagates and the request fails; nothing here turns a failed check
/// into a pass. A token with neither marker is an own sign-in and is not this type's business.
/// </para>
/// </remarks>
public static class ViewAsSessionValidation
{
    /// <summary>Validates a view-as token against its session; leaves every other token alone.</summary>
    /// <param name="context">The bearer handler's context for a token whose signature and lifetime already passed.</param>
    /// <returns>Resolves once the token has been accepted or failed.</returns>
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null || !ActorClaimReader.IsViewAs(principal))
        {
            return;
        }

        if (!ActorClaimReader.TryRead(principal, out var actor)
            || !Guid.TryParse(principal.FindFirst(PanelClaimTypes.SessionId)?.Value, out var sessionId)
            || !Guid.TryParse(principal.FindFirst(PanelClaimTypes.UserId)?.Value, out var viewedUserId))
        {
            context.Fail("The view-as token is malformed.");
            return;
        }

        var verifier = context.HttpContext.RequestServices.GetRequiredService<ViewAsSessionVerifier>();
        var live = await verifier.IsLiveAsync(
            sessionId,
            viewedUserId,
            actor.UserId,
            actor.SessionId,
            context.HttpContext.RequestAborted);

        if (!live)
        {
            context.Fail("The view-as session has ended.");
        }
    }
}
