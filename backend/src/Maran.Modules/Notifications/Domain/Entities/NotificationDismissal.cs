namespace Maran.Modules.Notifications.Domain.Entities;

/// <summary>
/// The fact that one administrator has dismissed one notification. The notification stays in the
/// feed for every other administrator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per administrator, so a row of its own.</b> A flag on <see cref="PanelNotification"/> could
/// only say "somebody dismissed this", and the second administrator would lose an entry they never
/// read. The pair <see cref="NotificationId"/> and <see cref="UserId"/> is the key, which is also what
/// makes dismissing twice impossible rather than merely unusual.
/// </para>
/// <para>
/// <see cref="UserId"/> is an administrator's id from Identity and is not a foreign key: a module
/// never references another module's tables (rules/architecture.md). The consequence is that
/// deleting an administrator does not remove the row by itself; the rows leave with their
/// notification at retention, which bounds how long a deleted administrator's id can outlive them.
/// </para>
/// </remarks>
public sealed class NotificationDismissal
{
    /// <summary>The notification that was dismissed.</summary>
    public Guid NotificationId { get; private set; }

    /// <summary>The administrator who dismissed it, from their own sign-in and never from a request body.</summary>
    public Guid UserId { get; private set; }

    /// <summary>When it was dismissed.</summary>
    public DateTimeOffset DismissedAt { get; private set; }

    /// <summary>Records that an administrator dismissed a notification.</summary>
    /// <param name="notificationId">The notification dismissed.</param>
    /// <param name="userId">The administrator who dismissed it.</param>
    /// <param name="dismissedAt">When, from <see cref="IClock"/>.</param>
    public NotificationDismissal(Guid notificationId, Guid userId, DateTimeOffset dismissedAt)
    {
        NotificationId = notificationId;
        UserId = userId;
        DismissedAt = dismissedAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private NotificationDismissal()
    {
    }
}
