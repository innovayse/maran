using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Requires that a view-as token is calling only an endpoint that declares, with
/// <see cref="ViewAsAccessAttribute"/>, that a view-as token may reach it (issues #317, #334).
/// </summary>
/// <remarks>
/// Attached to every panel policy, like <see cref="TwoFactorEnrolmentCompleteRequirement"/>, because
/// being a view-as token is a property of the SESSION: opting endpoints into the check one by one
/// would leave the next endpoint open by default. The requirement is an allow-list for the same reason. The refusal is a 403 — the caller is signed in and
/// knows exactly what they are holding, so a 404 would only make a deliberate restriction look like a
/// broken panel.
/// </remarks>
public sealed class ViewAsBoundaryRequirement : IAuthorizationRequirement
{
}
