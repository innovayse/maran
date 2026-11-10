using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Queries.ListMailQueue;

/// <summary>
/// Reads one page of the host's mail queue, optionally searched. Bound straight from the query string
/// (<c>GET /api/v1/mail-queue?page=2&amp;search=example.net&amp;sort=-size</c>);
/// <see cref="ListMailQueueQueryValidator"/> holds every member to its bounds before the handler runs.
/// </summary>
/// <remarks>
/// The list is host-wide by nature and the route is administrators only; there is no account id to
/// pass. The agent lists a bounded number of messages in one call and the panel pages that list in
/// memory, because the agent has no page parameter.
/// </remarks>
/// <param name="Page">The 1-based page requested; a page past the end answers empty with the true total.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">
/// A term matched literally, ignoring case, inside the queue id or the sender, or <see cref="SearchTerm.None"/>.
/// </param>
/// <param name="Sort">
/// One of <see cref="MailQueueSortFields"/> either way (<c>sender</c>, <c>-size</c>), or
/// <see cref="SortRequest.Default"/> for the newest first.
/// </param>
public sealed record ListMailQueueQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default) : IPagedQuery;
