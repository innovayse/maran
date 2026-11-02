using System.Text.Json.Serialization;
using Maran.Modules.Cron.Domain.Enums;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Cron.Queries.ListCronRuns;

/// <summary>One server page of an entry's runs started by hand, newest first (issue #158).</summary>
/// <param name="AccountId">
/// The account whose entry it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">Rows per page, one of the allowed sizes.</param>
/// <param name="Search">Text the run's output must contain, matched literally; none for every run.</param>
/// <param name="Sort">The sort key, or the default (newest first).</param>
/// <param name="Outcome">Only runs that ended this way; null for every outcome.</param>
/// <param name="EntryId">The entry whose runs are listed, established by the server from the route and
/// stamped by the action. Never bound from the query string.</param>
public sealed record ListCronRunsQuery(
    Guid AccountId,
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    CronRunOutcome? Outcome = null,
    [property: JsonIgnore][property: BindNever][BindNever] string EntryId = "") : IPagedQuery;
