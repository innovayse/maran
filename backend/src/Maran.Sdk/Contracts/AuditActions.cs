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

    /// <summary>A site was created: its document root, vhost and pool now exist on the host.</summary>
    public const string SiteCreated = "SiteCreated";

    /// <summary>A site was rebound to a different installed PHP version.</summary>
    public const string SitePhpVersionChanged = "SitePhpVersionChanged";

    /// <summary>A site was returned to normal serving.</summary>
    public const string SiteEnabled = "SiteEnabled";

    /// <summary>A site was made to serve a suspension response instead of its content.</summary>
    public const string SiteDisabled = "SiteDisabled";

    /// <summary>A site was removed: its vhost is gone, its files are left alone.</summary>
    public const string SiteDeleted = "SiteDeleted";

    /// <summary>
    /// A site's log was opened for tailing. Recorded on the refusals too: a request for a log the
    /// caller may not read is a probe, and the journal is where a pattern of them becomes visible.
    /// </summary>
    public const string SiteLogTailed = "SiteLogTailed";

    /// <summary>A certificate was ordered from a certificate authority and installed.</summary>
    public const string CertificateIssued = "CertificateIssued";

    /// <summary>A certificate the customer supplied was installed.</summary>
    public const string CertificateInstalled = "CertificateInstalled";

    /// <summary>A certificate was removed and its site returned to plain HTTP.</summary>
    public const string CertificateRemoved = "CertificateRemoved";

    /// <summary>
    /// An unattended renewal ran for one certificate. Recorded on success and on failure alike:
    /// renewal has no operator watching it, so the journal is where a certificate that has quietly
    /// stopped renewing becomes visible before the site goes dark.
    /// </summary>
    public const string CertificateRenewed = "CertificateRenewed";
}
