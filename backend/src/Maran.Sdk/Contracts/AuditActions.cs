namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable action names written into the audit journal. Constants rather than an enum:
/// a marketplace module records actions this assembly was never compiled knowing about, and an
/// enum could not be extended from outside.
/// </summary>
public static class AuditActions
{
    /// <summary>A user signed in.</summary>
    public const string LoginSucceeded = "LoginSucceeded";

    /// <summary>A sign-in attempt was refused.</summary>
    public const string LoginFailed = "LoginFailed";

    /// <summary>A user signed out of one device.</summary>
    public const string LoggedOut = "LoggedOut";

    /// <summary>A user signed out of every device.</summary>
    public const string LoggedOutEverywhere = "LoggedOutEverywhere";

    /// <summary>A session was ended from the sessions screen.</summary>
    public const string SessionRevoked = "SessionRevoked";

    /// <summary>A refresh token was presented after it had already been rotated.</summary>
    public const string RefreshTokenReuseDetected = "RefreshTokenReuseDetected";

    /// <summary>A user enrolled a second factor.</summary>
    public const string TwoFactorEnabled = "TwoFactorEnabled";

    /// <summary>A user removed their second factor.</summary>
    public const string TwoFactorDisabled = "TwoFactorDisabled";

    /// <summary>A recovery code was spent in place of a TOTP code.</summary>
    public const string RecoveryCodeUsed = "RecoveryCodeUsed";

    /// <summary>A password was changed.</summary>
    public const string PasswordChanged = "PasswordChanged";

    /// <summary>The panel's first administrator was created from the installer's one-time token.</summary>
    public const string AdministratorCreated = "AdministratorCreated";
}
