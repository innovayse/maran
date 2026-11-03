using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Requires that a customer's own token is not speaking for an account their membership has been
/// suspended from (issues #326, #331).
/// </summary>
/// <remarks>
/// Attached to the customer-level policy by the Host (<c>RolePolicies</c>), like
/// <see cref="TwoFactorEnrolmentCompleteRequirement"/>, because whether the account is suspended is a
/// property of the REQUEST's account and not of any one endpoint: opting endpoints in one by one would
/// leave the next tenant endpoint open by default. The refusal is the same 409
/// <c>AccountMembershipSuspended</c> sign-in, refresh and switching already answer; it names the
/// caller's own account, so it discloses nothing about another tenant.
/// </remarks>
public sealed class AccountMembershipActiveRequirement : IAuthorizationRequirement
{
}
