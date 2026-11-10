using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListMigrationJobAccounts;

/// <summary>Lists one page of a job's accounts (issue #197, phase 2).</summary>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">The page size.</param>
/// <param name="Search">Matches the account name or the primary domain.</param>
/// <param name="Sort">The sort.</param>
/// <param name="State">Only accounts in this state, by name.</param>
/// <param name="JobId">Server-established: the job, from the route.</param>
public sealed record ListMigrationJobAccountsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    FilterValue State = default,
    [property: System.Text.Json.Serialization.JsonIgnore]
    [property: Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] Guid JobId = default) : IPagedQuery;
