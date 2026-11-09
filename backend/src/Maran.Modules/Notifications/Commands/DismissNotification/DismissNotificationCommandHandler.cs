using Maran.Modules.Notifications.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Notifications.Commands.DismissNotification;

/// <summary>Handles <see cref="DismissNotificationCommand"/> by dismissing for the caller and journalling a real change.</summary>
/// <remarks>
/// Dismissing an id that does not exist, or that the caller already dismissed, succeeds and writes
/// nothing — neither a row nor a journal entry — so repeating the request is harmless and the answer
/// does not say whether the id ever existed.
/// </remarks>
public sealed class DismissNotificationCommandHandler
{
    /// <summary>Writes the caller's dismissals.</summary>
    private readonly NotificationDismissalWriter _writer;

    /// <summary>The panel's append-only journal.</summary>
    private readonly NotificationsAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="writer">Writes the caller's dismissals.</param>
    /// <param name="journal">The panel's append-only journal.</param>
    public DismissNotificationCommandHandler(NotificationDismissalWriter writer, NotificationsAuditJournal journal)
    {
        _writer = writer;
        _journal = journal;
    }

    /// <summary>Dismisses one notification.</summary>
    /// <param name="command">The notification to dismiss.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, whether or not anything changed.</returns>
    public async Task<Result> HandleAsync(DismissNotificationCommand command, CancellationToken cancellationToken)
    {
        var written = await _writer.DismissAsync(command.NotificationId, cancellationToken);
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
