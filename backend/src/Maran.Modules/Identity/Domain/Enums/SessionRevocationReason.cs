namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>Why a <see cref="Entities.Session"/> stopped being usable. Recorded so the audit screen can say more than "gone".</summary>
public enum SessionRevocationReason
{
    /// <summary>The user signed out of this device.</summary>
    Logout,

    /// <summary>The user signed out of every device at once.</summary>
    LogoutAll,

    /// <summary>The session's refresh token was exchanged for a new one, as every refresh does.</summary>
    Rotated,

    /// <summary>
    /// A refresh token that had already been rotated was presented again. Either a stolen cookie is
    /// being replayed or the legitimate user is racing a thief; both mean the whole chain dies.
    /// </summary>
    ReuseDetected,

    /// <summary>An administrator ended the session from the sessions screen.</summary>
    RevokedByAdmin,

    /// <summary>The user's password changed, which invalidates everything issued against the old one.</summary>
    PasswordChanged,

    /// <summary>
    /// The login's hosting account was suspended. Closing the sessions already open is the half of a
    /// suspension that actually matters: locking the login alone leaves whoever is already signed in
    /// working normally until their access token expires.
    /// </summary>
    AccountSuspended,

    /// <summary>The operator viewing as this user pressed Back, ending the view-as session.</summary>
    ViewAsEnded,

    /// <summary>
    /// The account's owner removed the login's access to the account the session was speaking for. The
    /// session ends with the access, so a removed team member does not keep a refresh token that
    /// renews a token for an account they no longer belong to.
    /// </summary>
    MembershipRemoved,
}
