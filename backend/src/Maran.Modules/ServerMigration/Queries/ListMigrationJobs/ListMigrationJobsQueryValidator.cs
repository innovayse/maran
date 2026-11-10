using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.ServerMigration.Queries.ListMigrationJobs;

/// <summary>Holds the job list to the paging bounds.</summary>
public sealed class ListMigrationJobsQueryValidator : PagedQueryValidator<ListMigrationJobsQuery, MigrationJob>
{
    /// <summary>Binds the validator to the sort whitelist.</summary>
    public ListMigrationJobsQueryValidator()
        : base(ListMigrationJobsSorts.Map)
    {
    }
}
