using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Persistence;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Notifications.Services;

/// <summary>
/// Records that the signed-in administrator dismissed notifications, and is the one place the feed's
/// "active for this administrator" rule is spelled for writing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The administrator comes from the sign-in, never from a request.</b> The caller is read from
/// <see cref="ICurrentUser"/>; a caller that is not an administrator dismisses nothing and the call
/// still succeeds, because the answer to "dismiss what is not yours to see" is the same as to
/// "dismiss what is already gone": nothing happens. The HTTP surface refuses such a caller before it
/// gets here; this is the fail-closed floor for a caller that reached it on the bus.
/// </para>
/// <para>
/// <b>Idempotent, including against itself.</b> A notification already dismissed by the caller, or
/// not existing, writes no row. Two simultaneous dismissals (a double click) can both pass that check
/// and collide on the key; the loser finds the row already there and the pass is repeated once, which
/// then has nothing left to write.
/// </para>
/// </remarks>
public sealed class NotificationDismissalWriter
{
    /// <summary>The Notifications module's database context.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>The authenticated principal whose dismissals these are.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The injected time source; never the ambient clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the writer.</summary>
    /// <param name="dbContext">The Notifications module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="clock">The time source stamping each dismissal.</param>
    public NotificationDismissalWriter(NotificationsDbContext dbContext, ICurrentUser currentUser, IClock clock)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>Dismisses for the caller either one notification or every one they have not dismissed.</summary>
    /// <param name="notificationId">The one notification to dismiss, or <c>null</c> for all of them.</param>
    /// <param name="cancellationToken">Cancels the reads and the write.</param>
    /// <returns>How many dismissals were written; zero when there was nothing left to dismiss.</returns>
    public async Task<int> DismissAsync(Guid? notificationId, CancellationToken cancellationToken)
    {
        if (_currentUser.Caller is not AdministratorCaller administrator)
        {
            return 0;
        }

        try
        {
            return await WriteAsync(administrator.UserId, notificationId, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent dismissal wrote the same key first. Start over from what is stored; the
            // second pass finds those rows and writes only what is still missing.
            _dbContext.ChangeTracker.Clear();
            return await WriteAsync(administrator.UserId, notificationId, cancellationToken);
        }
    }

    /// <summary>One pass: finds what the administrator has not dismissed and stores a dismissal for each.</summary>
    /// <param name="userId">The administrator.</param>
    /// <param name="notificationId">The one notification, or <c>null</c> for every one.</param>
    /// <param name="cancellationToken">Cancels the reads and the write.</param>
    /// <returns>How many dismissals were written.</returns>
    private async Task<int> WriteAsync(Guid userId, Guid? notificationId, CancellationToken cancellationToken)
    {
        var candidates = _dbContext.PanelNotifications
            .Where(notification => !_dbContext.NotificationDismissals.Any(dismissal =>
                dismissal.NotificationId == notification.Id && dismissal.UserId == userId));

        if (notificationId is { } only)
        {
            candidates = candidates.Where(notification => notification.Id == only);
        }

        var ids = await candidates.Select(notification => notification.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        var at = _clock.UtcNow;
        _dbContext.NotificationDismissals.AddRange(ids.Select(id =>
        {
            return new NotificationDismissal(id, userId, at);
        }));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ids.Count;
    }
}
