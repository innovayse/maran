using Maran.Modules.Tasks.Domain.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Tasks.Queries.ListTasks;

/// <summary>
/// Reads one page of the background tasks the caller may see, optionally searched and narrowed to a
/// status, a kind, an account and a window of start times. Bound straight from the query string
/// (<c>GET /api/v1/tasks?page=2&amp;search=shop&amp;status=failed&amp;kind=BackupCreate</c>);
/// <see cref="ListTasksQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who sees what never comes from this query.</b> The context's query filter decides it — an
/// administrator every task, a customer only the tasks recorded for their own account with an
/// audience that admits them (<c>TaskAudience.AccountOwner</c>). Every member here only narrows that
/// scope, ANDed with the filter, so a customer naming another account's id, or a kind only an
/// administrator's tasks carry, gets an empty page with a total of zero.
/// </para>
/// <para>
/// The status filter is written on the query string in the camelCase the JSON answers carry
/// (<c>failed</c>); a number naming no member is refused while binding, with
/// <c>RequestParameterInvalid</c>.
/// </para>
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally inside the task's subject — the domain or account name it acts on — or
/// <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="TaskSortFields"/> either way (<c>subject</c>, <c>-startedAt</c>), or
/// <see cref="SortRequest.Default"/> for newest first.
/// </param>
/// <param name="AccountId">
/// Only tasks concerning this account, or <c>null</c> for every task the caller may see. Bound from
/// the <c>accountId</c> query-string parameter.
/// </param>
/// <param name="Status">Only tasks in this state, or <c>null</c> for every state.</param>
/// <param name="Kind">
/// Only tasks of this machine-stable kind (<c>BackupCreate</c>), or <see cref="FilterValue.None"/> for
/// every kind.
/// </param>
/// <param name="From">Only tasks started at or after this instant, or <c>null</c> for no lower bound.</param>
/// <param name="To">
/// Only tasks started at or before this instant, or <c>null</c> for no upper bound. When both bounds
/// are given, <paramref name="From"/> must not be later than this.
/// </param>
public sealed record ListTasksQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    Guid? AccountId = null,
    PanelTaskStatus? Status = null,
    FilterValue Kind = default,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IPagedQuery;
