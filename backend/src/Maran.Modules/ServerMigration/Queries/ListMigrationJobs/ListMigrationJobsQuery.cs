using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListMigrationJobs;

/// <summary>Lists one page of the whole-server migration jobs (issue #197, phase 2).</summary>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">The page size.</param>
/// <param name="Search">Matches the job's label.</param>
/// <param name="Sort">The sort.</param>
/// <param name="State">Only jobs in this state, by name.</param>
public sealed record ListMigrationJobsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    FilterValue State = default) : IPagedQuery;
