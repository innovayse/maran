using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Monitoring.Queries.ListAccountDiskUsage;

/// <summary>
/// Reads one page of what the panel's hosting accounts occupy on disk, beside what each plan allows,
/// optionally searched by user name. Bound straight from the query string
/// (<c>GET /api/v1/monitoring/accounts-disk?page=2&amp;search=ali&amp;sort=-username</c>);
/// <see cref="ListAccountDiskUsageQueryValidator"/> holds every member to its bounds before the handler
/// runs.
/// </summary>
/// <remarks>
/// <para>
/// There is no account id to pass and therefore nothing a caller can probe a tenant with: the listing
/// is host-wide by nature, and the route is administrators only. The search narrows by user name and
/// nothing else.
/// </para>
/// <para>
/// <b>Used bytes cannot be a sort, and that is not an omission.</b> The page is chosen from the panel's
/// own accounts BEFORE the agent is asked, and the agent then measures only that page's homes — which is
/// the whole point of paging this list, since a measurement walks a home directory tree. Ordering by
/// usage would need every account measured to know which ones belong on the first page, which is the
/// host-wide walk the paging exists to avoid. Every sort in <see cref="ListAccountDiskUsageSorts"/> is
/// therefore something the panel knows without asking the agent.
/// </para>
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally, ignoring case, inside the account's system user name, or
/// <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="AccountDiskUsageSortFields"/> either way (<c>username</c>, <c>-quotaBytes</c>), or
/// <see cref="SortRequest.Default"/> for user name ascending.
/// </param>
public sealed record ListAccountDiskUsageQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
