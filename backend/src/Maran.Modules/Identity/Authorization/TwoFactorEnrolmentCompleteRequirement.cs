using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Requires that the caller is not mid-way through a forced two-factor enrolment — or, if they are,
/// that the endpoint they are calling is part of that enrolment.
/// </summary>
/// <remarks>
/// <para>
/// <b>This refusal answers 403, and it is the ONE deliberate exception to this plan's
/// 404-not-403 rule.</b> Everywhere else a caller who may not have a thing is told the thing does
/// not exist, because 403 confirms existence to somebody who was probing. Here the caller is
/// already authenticated, the panel already knows exactly who they are, and they are not probing —
/// they are being steered. A 404 would tell a legitimate administrator that the panel they just
/// signed into has no screens, which is indistinguishable from the panel being broken; a 403 tells
/// their SPA to send them to the enrolment page. Nothing is disclosed that the caller's own token
/// does not already say.
/// </para>
/// <para>
/// <b>It is attached to every policy the panel has, not to individual endpoints.</b> The steering is
/// a property of the SESSION rather than of any resource, so opting endpoints into it one by one
/// would mean the next endpoint anybody adds is reachable by a steered administrator by default.
/// The exemption is the thing that is declared per endpoint
/// (<see cref="AllowDuringTwoFactorEnrolmentAttribute"/>), because forgetting THAT locks somebody
/// out of a screen — a loud, reported failure — rather than quietly opening one.
/// </para>
/// </remarks>
public sealed class TwoFactorEnrolmentCompleteRequirement : IAuthorizationRequirement
{
}
