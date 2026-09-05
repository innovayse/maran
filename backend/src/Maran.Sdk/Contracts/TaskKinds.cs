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
}
