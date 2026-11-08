using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Persistence;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Notifications.Queries.GetNotificationCount;

/// <summary>Handles <see cref="GetNotificationCountQuery"/> with a bounded count of the caller's active entries.</summary>
/// <remarks>
/// The read stops at <see cref="Cap"/> + 1 rows, so a feed that has grown never costs the badge more
/// than a bounded scan. A caller that is not an administrator is counted zero, as the list shows it
/// nothing.
/// </remarks>
public sealed class GetNotificationCountQueryHandler
{
    /// <summary>The largest number the badge shows; one more is "more than this".</summary>
    public const int Cap = 99;

    /// <summary>The Notifications module's database context.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>The authenticated principal whose dismissals decide what is active.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Notifications module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public GetNotificationCountQueryHandler(NotificationsDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    /// <summary>Counts the active entries up to the cap.</summary>
    /// <param name="query">The request; it carries nothing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The capped count and whether it was capped.</returns>
    public async Task<Result<NotificationCountDto>> HandleAsync(
        GetNotificationCountQuery query,
        CancellationToken cancellationToken)
    {
        if (_currentUser.Caller is not AdministratorCaller administrator)
        {
            return Result<NotificationCountDto>.Ok(new NotificationCountDto(0, false));
        }

        var userId = administrator.UserId;
        var counted = await _dbContext.PanelNotifications.AsNoTracking()
            .Where(notification => !_dbContext.NotificationDismissals.Any(dismissal =>
                dismissal.NotificationId == notification.Id && dismissal.UserId == userId))
            .Take(Cap + 1)
            .CountAsync(cancellationToken);

        return Result<NotificationCountDto>.Ok(new NotificationCountDto(Math.Min(counted, Cap), counted > Cap));
    }
}
