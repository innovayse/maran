namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable kind names written onto a panel task. Constants rather than an enum, for the
/// reason <see cref="AuditActions"/> is: a marketplace module records kinds this assembly was never
/// compiled knowing about, and an enum could not be extended from outside.
/// </summary>
/// <remarks>
/// A kind is deliberately NOT an audit action, and the two lists stay apart even where they name the
/// same operation. An audit entry answers "who did this, and did it take effect"; a task answers "is
/// this still running, and how far has it got". They have different lifetimes — the journal is
/// append-only forever, tasks are operational state — and folding them together would make every new
/// progress bar an addition to the permanent security record.
/// </remarks>
public static class TaskKinds
{
    /// <summary>A certificate is being ordered from a certificate authority and installed.</summary>
    public const string CertificateIssue = "CertificateIssue";

    /// <summary>An unattended renewal is re-ordering and reinstalling one certificate.</summary>
    public const string CertificateRenewal = "CertificateRenewal";

    /// <summary>
    /// A hosting account is being deleted, together with everything every module holds against it.
    /// The longest and most destructive operation the panel offers, and the one an operator most
    /// needs to be able to watch rather than guess at.
    /// </summary>
    public const string AccountDeletion = "AccountDeletion";

    /// <summary>
    /// A hosting account is being suspended: every module driving something on its behalf is asked
    /// to stop, and the host is then asked what it can be observed to be serving.
    /// </summary>
    /// <remarks>
    /// It earns a task rather than being a plain request/response because the stage line is the only
    /// place an operator is told what the suspension could NOT check — cron and the SFTP logins are
    /// not covered by it, and a completion with no such qualification would read as a full stop.
    /// </remarks>
    public const string AccountSuspension = "AccountSuspension";

    /// <summary>A suspended hosting account is being reactivated and its enabled sites restored.</summary>
    public const string AccountResumption = "AccountResumption";

    /// <summary>
    /// An account is being backed up: its home directory archived and each of its databases dumped.
    /// The panel's longest routine operation, and the one whose progress an operator watches rather
    /// than waits on.
    /// </summary>
    public const string BackupCreate = "BackupCreate";

    /// <summary>
    /// A backup is being restored over a live account: its home replaced and each of its databases
    /// dropped and reloaded.
    /// </summary>
    /// <remarks>
    /// It earns a task more than any other operation the panel offers, and for a reason the others
    /// do not have: it can fail HALFWAY, with some databases replaced and some not, and the stage
    /// line is where an operator learns which state the account is in while it is happening. A
    /// restore watched only through a request that has not answered yet is a restore nobody can say
    /// anything about until it is over.
    /// </remarks>
    public const string BackupRestore = "BackupRestore";
}
