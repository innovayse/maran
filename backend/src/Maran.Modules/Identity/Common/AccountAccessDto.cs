using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Common;

/// <summary>
/// How the signed-in person stands towards the account their sign-in is acting for: whether they own
/// it or were added to it, and which gated features they may use there.
/// </summary>
/// <remarks>
/// <b>Advice for the screen, never the boundary.</b> It lets the SPA leave out what the person cannot
/// use; every gated endpoint asks the same question itself on each call.
/// </remarks>
/// <param name="Role">Whether the person owns the account or is a team member of it.</param>
/// <param name="RoleDisplayName">What that role is called, resolved for the request's culture.</param>
/// <param name="Features">
/// The single features the person may use. For an owner, every feature (an owner is not restricted by
/// grants; the account's plan is the only bound); for a member, the features the owner gave them.
/// </param>
public sealed record AccountAccessDto(
    MembershipRole Role,
    string RoleDisplayName,
    IReadOnlyList<AccountFeature> Features);
