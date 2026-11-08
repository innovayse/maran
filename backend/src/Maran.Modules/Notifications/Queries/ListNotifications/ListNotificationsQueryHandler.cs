using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Mappers;
using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Services;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Notifications.Queries.ListNotifications;

/// <summary>Handles <see cref="ListNotificationsQuery"/> by reading one page of the caller's active entries.</summary>
/// <remarks>
/// An entry is active for the caller when the caller has no dismissal of it. A caller that is not an
/// administrator is shown an empty page: the feed is the operator's, and answering a customer with
/// entries, or with an error that confirms there are some, would be the wrong failure.
/// </remarks>
public sealed class ListNotificationsQueryHandler
{
    /// <summary>The Notifications module's database context.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>The authenticated principal whose dismissals decide what is active.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Words each entry in the request's culture.</summary>
    private readonly NotificationTextResolver _text;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Notifications module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="text">Words each entry in the request's culture.</param>
    public ListNotificationsQueryHandler(
        NotificationsDbContext dbContext,
        ICurrentUser currentUser,
        NotificationTextResolver text)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _text = text;
    }

    /// <summary>Filters, searches, and answers the requested page with the total that matched.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<NotificationDto>>> HandleAsync(
        ListNotificationsQuery query,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.Caller is AdministratorCaller administrator ? administrator.UserId : Guid.Empty;

        // Guid.Empty stands for "not an administrator". The first clause of the filter is what makes
        // such a caller see nothing; without it the dismissal test would find no dismissals for
        // Guid.Empty and show every entry.
        var entries = _dbContext.PanelNotifications.AsNoTracking()
            .Where(notification => userId != Guid.Empty && !_dbContext.NotificationDismissals.Any(dismissal =>
                dismissal.NotificationId == notification.Id && dismissal.UserId == userId));

        if (query.Source is { } source)
        {
            entries = entries.Where(notification => notification.Source == source);
        }

        if (!query.Search.IsNone)
        {
            var pattern = LikePattern.Contains(query.Search.Value);
            entries = entries.Where(notification =>
                EF.Functions.ILike(notification.Subject, pattern, LikePattern.EscapeCharacter));
        }

        var page = await entries.ToPagedAsync(query, ListNotificationsSorts.Map, cancellationToken);

        return Result<PagedResult<NotificationDto>>.Ok(page.Map(notification =>
        {
            var (title, detail) = _text.Resolve(notification);
            return NotificationMapper.From(notification, title, detail);
        }));
    }
}
