using Maran.Modules.Backups.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Backups.Queries.ListBackups;

/// <summary>
/// The orders the backup history accepts. <see cref="ListBackupsQueryValidator"/> refuses any other
/// sort and <see cref="ListBackupsQueryHandler"/> orders by exactly these, so the whitelist is written
/// once.
/// </summary>
public static class ListBackupsSorts
{
    /// <summary>
    /// <see cref="BackupSortFields.StartedAt"/> and <see cref="BackupSortFields.Size"/>, newest first
    /// when none is named — the order the history had before it was paged — with ties broken by the
    /// backup id.
    /// </summary>
    public static readonly SortMap<Backup> Map = new SortMapBuilder<Backup>()
        .By(BackupSortFields.StartedAt, backup => backup.StartedAt)
        .By(BackupSortFields.Size, backup => backup.SizeBytes)
        .DefaultTo(BackupSortFields.StartedAt, SortDirection.Descending)
        .TieBreaker(backup => backup.Id);
}
