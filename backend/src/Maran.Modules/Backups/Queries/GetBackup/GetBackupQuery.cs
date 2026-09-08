namespace Maran.Modules.Backups.Queries.GetBackup;

/// <summary>Reads one backup. Another account's answers not-found, never forbidden.</summary>
/// <param name="BackupId">The backup to read.</param>
public sealed record GetBackupQuery(Guid BackupId);
