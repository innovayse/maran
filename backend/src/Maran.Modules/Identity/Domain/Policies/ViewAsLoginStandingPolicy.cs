using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The stateless rule that reads whether a login can be viewed as, and which reason refuses it
/// (issue #416). It never widens <see cref="User.MayAuthenticate"/>: a standing is
/// <see cref="ViewAsLoginStanding.Available"/> only when that is true and the membership allows it.
/// </summary>
/// <remarks>
/// The order is the point. A suspension is checked BEFORE a pending invitation, because a reseller can
/// be suspended while still invited and an account can be suspended before its owner accepted: the
/// operator's next step is then to resume it, and telling them to wait for the owner would hide that
/// the login is blocked for a second reason. Only a login that is invited and not otherwise blocked is
/// reported as <see cref="ViewAsLoginStanding.InvitationPending"/>.
/// </remarks>
public static class ViewAsLoginStandingPolicy
{
    /// <summary>Reads the standing of a login that is about to be viewed as.</summary>
    /// <param name="login">The login that would be viewed as.</param>
    /// <param name="membershipSuspended">
    /// Whether the login's membership of the viewed account is suspended; false for a reseller's
    /// login, which holds no membership.
    /// </param>
    /// <returns>
    /// <see cref="ViewAsLoginStanding.Unavailable"/> for a suspended login or membership,
    /// <see cref="ViewAsLoginStanding.InvitationPending"/> for an unblocked login with no password yet,
    /// otherwise <see cref="ViewAsLoginStanding.Available"/>.
    /// </returns>
    public static ViewAsLoginStanding Of(User login, bool membershipSuspended)
    {
        ArgumentNullException.ThrowIfNull(login);

        if (login.IsSuspended || membershipSuspended)
        {
            return ViewAsLoginStanding.Unavailable;
        }

        if (login.HasPendingInvitation)
        {
            return ViewAsLoginStanding.InvitationPending;
        }

        return login.MayAuthenticate ? ViewAsLoginStanding.Available : ViewAsLoginStanding.Unavailable;
    }
}
