using System.Diagnostics;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Mappers;

/// <summary>
/// Restates a refused <see cref="ViewAsLoginStanding"/> as the failure a view-as start answers with
/// (issue #416), so the account and the reseller start handlers cannot disagree about it.
/// </summary>
public static class ViewAsLoginRefusalMapper
{
    /// <summary>Names the failure for a standing.</summary>
    /// <param name="standing">The standing the policy read.</param>
    /// <returns>
    /// Null for <see cref="ViewAsLoginStanding.Available"/>; <c>ViewAsInvitationPending</c> (409) for a
    /// login that has not accepted its invitation; <c>ViewAsLoginUnavailable</c> (409) for a suspended one.
    /// </returns>
    public static Error? From(ViewAsLoginStanding standing)
    {
        switch (standing)
        {
            case ViewAsLoginStanding.Available:
                return null;
            case ViewAsLoginStanding.InvitationPending:
                return Error.Of(nameof(ErrorMessages.ViewAsInvitationPending), ErrorType.Conflict);
            case ViewAsLoginStanding.Unavailable:
                return Error.Of(nameof(ErrorMessages.ViewAsLoginUnavailable), ErrorType.Conflict);
            default:
                throw new UnreachableException("ViewAsLoginStanding has exactly three cases.");
        }
    }
}
