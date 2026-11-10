using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListMigrationJobs;

/// <summary>The sort whitelist of the job list.</summary>
public static class ListMigrationJobsSorts
{
    /// <summary>The whitelist, newest first by default.</summary>
    public static readonly SortMap<MigrationJob> Map = new SortMapBuilder<MigrationJob>()
        .By(MigrationJobSortFields.Name, job => job.Name)
        .By(MigrationJobSortFields.State, job => job.State)
        .By(MigrationJobSortFields.CreatedAt, job => job.CreatedAt)
        .DefaultTo(MigrationJobSortFields.CreatedAt, SortDirection.Descending)
        .TieBreaker(job => job.Id);
}
