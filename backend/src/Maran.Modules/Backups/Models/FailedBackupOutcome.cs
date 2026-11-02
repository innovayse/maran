namespace Maran.Modules.Backups.Models;

/// <summary>A create run that produced no usable artifact.</summary>
/// <param name="FailureCode">The machine-stable code naming what went wrong.</param>
public sealed record FailedBackupOutcome(string FailureCode) : BackupRunOutcome;
