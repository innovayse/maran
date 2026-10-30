namespace Maran.Modules.Backups.Common;

/// <summary>
/// The backup history's one attention item: how many accounts have no good copy from their latest
/// attempt, and the newest such failure, which is enough to name when it is the only one.
/// </summary>
/// <param name="UnrecoveredAccounts">
/// How many accounts' most recent FINISHED backup failed. A failure a later completed run has
/// superseded is history and is not counted; a still-running run has no outcome and neither raises
/// nor clears one.
/// </param>
/// <param name="Newest">
/// The newest of those failures, or <c>null</c> exactly when <paramref name="UnrecoveredAccounts"/>
/// is zero.
/// </param>
public sealed record BackupAttentionDto(int UnrecoveredAccounts, BackupDto? Newest);
