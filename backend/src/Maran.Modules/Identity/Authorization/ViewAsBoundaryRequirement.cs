using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Requires that a view-as token is not calling an endpoint marked
/// <see cref="RefusedWhileViewingAsAttribute"/> (issue #317).
/// </summary>
/// <remarks>
/// Attached to every panel policy, like <see cref="TwoFactorEnrolmentCompleteRequirement"/>, because
/// being a view-as token is a property of the SESSION: opting endpoints into the check one by one
/// would leave the next endpoint open by default. The refusal is a 403 — the caller is signed in and
/// knows exactly what they are holding, so a 404 would only make a deliberate restriction look like a
/// broken panel.
/// </remarks>
public sealed class ViewAsBoundaryRequirement : IAuthorizationRequirement
{
}
