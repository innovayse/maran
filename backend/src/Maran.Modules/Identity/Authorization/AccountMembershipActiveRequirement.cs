using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Requires that a customer's own token is speaking for an account their membership of is still live:
/// neither suspended (issues #326, #331) nor removed (issue #340).
/// </summary>
/// <remarks>
/// Attached to the customer-level policy by the Host (<c>RolePolicies</c>), like
/// <see cref="TwoFactorEnrolmentCompleteRequirement"/>, because whether the account is suspended is a
/// property of the REQUEST's account and not of any one endpoint: opting endpoints in one by one would
/// leave the next tenant endpoint open by default. A suspension is refused with the same 409
/// <c>AccountMembershipSuspended</c> sign-in, refresh and switching already answer; it names the
/// caller's own account, so it discloses nothing about another tenant. A removed membership is the
/// generic 403, because the person has no standing left to name.
/// </remarks>
public sealed class AccountMembershipActiveRequirement : IAuthorizationRequirement
{
}
