namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// Whether a login can be viewed as, and, when it cannot, which of the two different reasons applies
/// (issue #416). The reasons are told apart because the operator's next step differs: a suspended
/// login is resumed, an invited one is waited for.
/// </summary>
public enum ViewAsLoginStanding
{
    /// <summary>The login may sign in, so a view-as session may be started for it.</summary>
    Available,

    /// <summary>The login's owner has not accepted the invitation yet, so no password exists to act under.</summary>
    InvitationPending,

    /// <summary>The login, or its membership of the viewed account, is suspended.</summary>
    Unavailable,
}
