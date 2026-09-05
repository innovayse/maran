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
}
