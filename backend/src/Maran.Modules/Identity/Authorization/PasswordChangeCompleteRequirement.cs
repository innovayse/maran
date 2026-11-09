using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Requires that the caller is not holding a first password somebody else chose — or, if they are,
/// that the endpoint they are calling is the one that replaces it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same shape as <see cref="TwoFactorEnrolmentCompleteRequirement"/>, on purpose.</b> It is
/// attached to every policy the panel has rather than to individual endpoints, because the gate is a
/// property of the SESSION and not of any resource: opting endpoints in one by one would make the next
/// endpoint anybody adds reachable, with a password the administrator knows, by default. The exemption
/// is what is declared per endpoint (<see cref="AllowDuringPasswordChangeAttribute"/>), because
/// forgetting THAT locks somebody out of a screen — a loud failure — instead of quietly opening one.
/// <c>PasswordChangeGateTests</c> in the architecture suite fails when a policy lacks this requirement.
/// </para>
/// <para>
/// <b>This refusal answers 403</b>, for the reason the two-factor steering does: the caller is
/// authenticated, the panel knows exactly who they are, and they are being steered, not probing. The
/// body carries the code <c>HostPasswordChangeRequired</c>, which discloses only what the caller's own
/// token already says.
/// </para>
/// </remarks>
public sealed class PasswordChangeCompleteRequirement : IAuthorizationRequirement
{
}
