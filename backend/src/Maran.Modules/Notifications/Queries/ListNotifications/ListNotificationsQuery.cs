using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Notifications.Queries.ListNotifications;

/// <summary>
/// Reads one page of the notifications the signed-in administrator has not dismissed, newest first,
/// optionally searched and narrowed to one source. Bound from the query string
/// (<c>GET /api/v1/notifications/feed?source=task</c>).
/// </summary>
/// <remarks>
/// "Active for the caller" is not a member: it is the administrator's own sign-in, so no request
/// can read the entries another administrator has or has not dismissed. A source naming no member is
/// refused while binding, with <c>RequestParameterInvalid</c>.
/// </remarks>
/// <param name="Page">The 1-based page requested.</param>
/// <param name="PageSize">Rows per page; one of <see cref="PageDefaults.AllowedSizes"/>.</param>
/// <param name="Search">A term matched literally inside the entry's subject, or <see cref="SearchTerm.None"/>.</param>
/// <param name="Sort">Either way of <see cref="NotificationSortFields"/>, or <see cref="SortRequest.Default"/> for newest first.</param>
/// <param name="Source">Only entries raised by this part of the panel, or <c>null</c> for every source.</param>
public sealed record ListNotificationsQuery(
    int Page = PageDefaults.FirstPage,
    int PageSize = PageDefaults.Size,
    SearchTerm Search = default,
    SortRequest Sort = default,
    NotificationSource? Source = null) : IPagedQuery;
