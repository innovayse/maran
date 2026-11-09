using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.ValueObjects;
using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Notifications.IntegrationEvents.Handlers;

/// <summary>Stores a notification another module raised, so the administrators' feed shows it.</summary>
/// <remarks>
/// <para>
/// <b>The only writer of the feed.</b> Producers publish <see cref="PanelNotificationRaised"/> after
/// their own commit and never touch this module's tables. The message travels on a local, in-memory
/// queue, so a notification raised in the instant a process dies is lost; that is acceptable because
/// the feed is a convenience over records that are not lost — the audit journal and the tasks page.
/// </para>
/// <para>
/// <b>It does not catch.</b> Unlike the mail handler, the envelope carries no secret, so letting a
/// failure reach the bus's own error handling exposes nothing, and swallowing it would hide a feed
/// that had stopped filling. Bounding the text is the entity's job (<see cref="PanelNotification"/>).
/// </para>
/// </remarks>
public sealed class PanelNotificationRaisedHandler
{
    /// <summary>The Notifications module's database context.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Notifications module's database context.</param>
    public PanelNotificationRaisedHandler(NotificationsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Stores one notification.</summary>
    /// <param name="message">What was raised.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the row is stored.</returns>
    public async Task HandleAsync(PanelNotificationRaised message, CancellationToken cancellationToken)
    {
        var notification = new PanelNotification(
            Guid.NewGuid(),
            message.Source,
            message.Code,
            message.Severity,
            message.Subject,
            PanelNotificationArguments.From(message.Arguments),
            message.DeepLink,
            message.OccurredAt);

        _dbContext.PanelNotifications.Add(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
