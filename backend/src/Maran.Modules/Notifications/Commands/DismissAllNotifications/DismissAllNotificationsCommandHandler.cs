using Maran.Modules.Notifications.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Notifications.Commands.DismissAllNotifications;

/// <summary>Handles <see cref="DismissAllNotificationsCommand"/> by dismissing everything active for the caller.</summary>
/// <remarks>Journalled once, with the number dismissed, and only when at least one was.</remarks>
public sealed class DismissAllNotificationsCommandHandler
{
    /// <summary>Writes the caller's dismissals.</summary>
    private readonly NotificationDismissalWriter _writer;

    /// <summary>The panel's append-only journal.</summary>
    private readonly NotificationsAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="writer">Writes the caller's dismissals.</param>
    /// <param name="journal">The panel's append-only journal.</param>
    public DismissAllNotificationsCommandHandler(NotificationDismissalWriter writer, NotificationsAuditJournal journal)
    {
        _writer = writer;
        _journal = journal;
    }

    /// <summary>Dismisses every active notification for the caller.</summary>
    /// <param name="command">The request; it carries only audit context.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, whether or not anything changed.</returns>
    public async Task<Result> HandleAsync(DismissAllNotificationsCommand command, CancellationToken cancellationToken)
    {
        var written = await _writer.DismissAsync(null, cancellationToken);
        if (written > 0)
        {
            await _journal.RecordRequestAsync(
                OutgoingMailAuditActions.NotificationsDismissed,
                written.ToString(System.Globalization.CultureInfo.InvariantCulture),
                command.IpAddress,
                command.UserAgent,
                succeeded: true,
                cancellationToken);
        }

        return Result.Ok();
    }
}
