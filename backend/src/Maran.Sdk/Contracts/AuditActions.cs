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

    /// <summary>
    /// A hosting account was created: its Linux user, home directory and disk quota now exist on
    /// the host. Recorded on the refusals too, because a name or a domain already taken is how one
    /// caller learns that another customer holds it.
    /// </summary>
    public const string AccountCreated = "AccountCreated";

    /// <summary>
    /// An account was suspended: its sites and services stopped while its data stayed. The event a
    /// customer whose sites went dark needs explained, and the one a billing system produces.
    /// </summary>
    public const string AccountSuspended = "AccountSuspended";

    /// <summary>An account's suspension was lifted and its sites and services put back.</summary>
    public const string AccountReactivated = "AccountReactivated";

    /// <summary>
    /// An account was deleted, and with it its system user, its home directory, every database it
    /// owned and every SFTP login it owned. The most destructive operation the panel offers and the
    /// one nothing else leaves a trace of: after it, the account name is all that is left to search
    /// for. Recorded on the refusals too — a deletion that got part-way through the cascade and then
    /// failed is journalled as the failure it was.
    /// </summary>
    public const string AccountDeleted = "AccountDeleted";

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

    /// <summary>A database and its dedicated user were created on the MySQL server.</summary>
    public const string DatabaseCreated = "DatabaseCreated";

    /// <summary>A database and its dedicated user were dropped, and the customer's data with them.</summary>
    public const string DatabaseDropped = "DatabaseDropped";

    /// <summary>
    /// A database user was given a new password. Recorded because it is the only recovery for a
    /// credential nobody keeps a copy of, so a reset somebody else performed is exactly the event a
    /// customer whose application stopped connecting needs explained. The entry names the database,
    /// never the value.
    /// </summary>
    public const string DatabasePasswordReset = "DatabasePasswordReset";

    /// <summary>An SFTP login was created on the host, jailed into its account's own chroot.</summary>
    public const string SftpUserCreated = "SftpUserCreated";

    /// <summary>
    /// An SFTP login was removed. The account's files are NOT removed with it — the login's home is
    /// the jail and the real home is bind-mounted inside it — so this entry records a revoked key
    /// rather than deleted data, which is exactly the distinction an operator reading it needs.
    /// </summary>
    public const string SftpUserDeleted = "SftpUserDeleted";

    /// <summary>
    /// An SFTP login was given a new password. Recorded because it is the only recovery for a
    /// credential nobody keeps a copy of, so a reset somebody else performed is exactly the event a
    /// customer whose client stopped connecting needs explained. The entry names the login, never
    /// the value.
    /// </summary>
    public const string SftpUserPasswordReset = "SftpUserPasswordReset";

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
