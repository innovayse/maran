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

    /// <summary>A scheduled command was added to an account's crontab.</summary>
    /// <remarks>
    /// The subject is the entry's identifier, NEVER the command. A cron command is the customer's
    /// own and is shown back to them in the panel, but it can legitimately carry a credential —
    /// <c>mysql -pSECRET</c>, a URL with a token — and this journal is append-only and never
    /// deleted. An identifier is enough to find the entry; a command here is a customer's password
    /// kept forever, in a place their operator reads.
    /// </remarks>
    public const string CronEntryCreated = "CronEntryCreated";

    /// <summary>A cron entry's schedule or command was replaced. Subject: the entry id, never the command.</summary>
    public const string CronEntryUpdated = "CronEntryUpdated";

    /// <summary>A cron entry was removed, together with the files that held its command and its last run.</summary>
    public const string CronEntryDeleted = "CronEntryDeleted";

    /// <summary>
    /// A cron entry was enabled or disabled. Recorded separately from an update because a disabled
    /// entry that still fires — or an enabled one that does not — is the failure an operator needs
    /// to be able to date.
    /// </summary>
    public const string CronEntryEnabledChanged = "CronEntryEnabledChanged";

    /// <summary>An account's cron environment variables were replaced. Names are recorded; values are not.</summary>
    public const string CronEnvironmentChanged = "CronEnvironmentChanged";

    /// <summary>A port was opened on the host firewall, optionally scoped to a source range.</summary>
    public const string FirewallRuleAllowed = "FirewallRuleAllowed";

    /// <summary>A port was closed on the host firewall.</summary>
    public const string FirewallRuleDenied = "FirewallRuleDenied";

    /// <summary>
    /// An address was banned from the host. Recorded whether a human or the brute-force detector
    /// asked for it, because "who banned this customer's office" is the question an operator will
    /// have, and an automatic ban with no entry is indistinguishable from a network fault.
    /// </summary>
    public const string AddressBanned = "AddressBanned";

    /// <summary>A ban was lifted before its timeout, or a permanent one was removed.</summary>
    public const string AddressUnbanned = "AddressUnbanned";

    /// <summary>The firewall whitelist was changed. Addresses on it are never banned automatically.</summary>
    public const string FirewallWhitelistChanged = "FirewallWhitelistChanged";

    /// <summary>
    /// The installer's recorded address became the whitelist's first row, at the panel's own
    /// initiative and with nobody signed in. Recorded because it is an exemption from every
    /// automatic ban that no request created, so without this entry the one row an operator never
    /// added is also the one row with no history.
    /// </summary>
    public const string FirewallWhitelistSeeded = "FirewallWhitelistSeeded";

    /// <summary>
    /// A brute-force ban was NOT applied because the address is whitelisted. Recorded as its own
    /// action rather than as a failure: nothing went wrong, and the absence of a ban an operator
    /// expected is exactly what this explains.
    /// </summary>
    public const string BanSkippedWhitelisted = "BanSkippedWhitelisted";

    /// <summary>The panel's outgoing mail settings were saved. The password is never part of the entry.</summary>
    public const string SmtpSettingsSaved = "SmtpSettingsSaved";

    /// <summary>A test message was sent to the administrator who asked for it.</summary>
    public const string TestMailSent = "TestMailSent";

    /// <summary>
    /// Mail was wanted and no SMTP settings exist, so nothing was sent. Recorded because the
    /// alternative is a password reset that silently never arrives.
    /// </summary>
    public const string MailSkippedNoSmtp = "MailSkippedNoSmtp";

    /// <summary>The mail server refused or could not be reached. Recorded with the reason, never with the body.</summary>
    public const string MailSendFailed = "MailSendFailed";

    /// <summary>A monitored condition crossed into alarm — a full disk, a stopped service.</summary>
    public const string AlertRaised = "AlertRaised";

    /// <summary>A monitored condition returned to normal. Paired with the raise so an operator can read the outage's length.</summary>
    public const string AlertResolved = "AlertResolved";

    /// <summary>
    /// A password reset was asked for. Recorded for EVERY request, including one naming an address
    /// no user holds — the journal is where a sweep through a list of guessed addresses becomes
    /// visible, and it is the only place that can see it, because the endpoint deliberately answers
    /// a known and an unknown address identically. The subject is the address as the caller typed
    /// it; the token never appears.
    /// </summary>
    public const string PasswordResetRequested = "PasswordResetRequested";

    /// <summary>
    /// A reset token was presented and refused — expired, already spent, or never issued. Its own
    /// action rather than a failed <see cref="PasswordChanged"/>, because a replayed token is the
    /// panel's only signal that a reset mail was intercepted, and it must not be lost among ordinary
    /// password changes. The entry never carries the token, only whether one was refused.
    /// </summary>
    public const string PasswordResetRefused = "PasswordResetRefused";

    /// <summary>
    /// The panel's security policy was changed: password length, forced two-factor, or the account
    /// lockout. Every one of those weakens or strengthens every account at once, so the change needs
    /// a date and an author more than most.
    /// </summary>
    public const string SecurityPolicySaved = "SecurityPolicySaved";
}
