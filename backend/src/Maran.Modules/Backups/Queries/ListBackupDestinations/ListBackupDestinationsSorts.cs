using Maran.Modules.Backups.Common;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Backups.Queries.ListBackupDestinations;

/// <summary>
/// The orders the destinations list accepts. <see cref="ListBackupDestinationsQueryValidator"/>
/// refuses any other sort and <see cref="ListBackupDestinationsQueryHandler"/> orders by exactly these.
/// </summary>
/// <remarks>
/// Over the wire shape rather than the entity because the list is paged in memory, after each row's
/// name has been put in the caller's language: the name a person sorts by is the one they read.
/// </remarks>
public static class ListBackupDestinationsSorts
{
    /// <summary>
    /// <see cref="BackupDestinationSortFields.Name"/> and <see cref="BackupDestinationSortFields.CreatedAt"/>,
    /// by name when none is named, with ties broken by the destination id.
    /// </summary>
    public static readonly SortMap<BackupDestinationDto> Map = new SortMapBuilder<BackupDestinationDto>()
        .By(BackupDestinationSortFields.Name, destination => destination.DisplayName)
        .By(BackupDestinationSortFields.CreatedAt, destination => destination.CreatedAt)
        .DefaultTo(BackupDestinationSortFields.Name, SortDirection.Ascending)
        .TieBreaker(destination => destination.Id);
}
