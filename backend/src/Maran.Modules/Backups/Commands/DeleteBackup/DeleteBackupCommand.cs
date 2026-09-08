namespace Maran.Modules.Backups.Commands.DeleteBackup;

/// <summary>
/// Deletes one backup: its stored artifact on the destination, and then the row that named it.
/// </summary>
/// <param name="BackupId">The backup to delete. Another account's answers not-found, never forbidden.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeleteBackupCommand(
    Guid BackupId,
    string IpAddress,
    string UserAgent);
