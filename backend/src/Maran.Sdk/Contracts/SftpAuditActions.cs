namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for sFTP login actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class SftpAuditActions
{
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

    /// <summary>
    /// The sessions an SFTP login had open were ended after its credential changed (issue #339). Recorded
    /// on the refusal too: a failure means the previous credential's sessions may still be open, which is
    /// the one thing an operator reading the reset above needs to know about it.
    /// </summary>
    public const string SftpUserSessionsEnded = "SftpUserSessionsEnded";

    /// <summary>
    /// A public key was added to an SFTP login. The entry names the login, the key's label and its
    /// fingerprint — never the key itself — and is recorded on the refusal too.
    /// </summary>
    public const string SftpKeyAdded = "SftpKeyAdded";

    /// <summary>
    /// A public key was removed from an SFTP login, which also ends the login's open sessions. The
    /// entry names the login, the key's label and its fingerprint, and is recorded on the refusal too:
    /// a failed removal may mean a revoked key's sessions are still open.
    /// </summary>
    public const string SftpKeyRemoved = "SftpKeyRemoved";
}
