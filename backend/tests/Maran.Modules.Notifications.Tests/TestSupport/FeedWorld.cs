using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.ValueObjects;
using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Notifications.Tests.TestSupport;

/// <summary>Builds the pieces of the feed over one in-memory database, seen as a chosen administrator.</summary>
public static class FeedWorld
{
    /// <summary>The instant seeded entries happened at, unless a test says otherwise.</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Adds one notification and saves it.</summary>
    /// <param name="context">The context to write through.</param>
    /// <param name="subject">What the entry is about.</param>
    /// <param name="occurredAt">When it happened; <see cref="Now"/> when omitted.</param>
    /// <param name="source">Which part of the panel raised it.</param>
    /// <returns>The stored entry.</returns>
    public static async Task<PanelNotification> SeedAsync(
        NotificationsDbContext context,
        string subject = "example.com",
        DateTimeOffset? occurredAt = null,
        NotificationSource source = NotificationSource.Task)
    {
        var notification = new PanelNotification(
            Guid.NewGuid(),
            source,
            "SiteNotFound",
            NotificationSeverity.Warning,
            subject,
            PanelNotificationArguments.None,
            NotificationDeepLinks.Tasks,
            occurredAt ?? Now);
        context.PanelNotifications.Add(notification);
        await context.SaveChangesAsync();

        return notification;
    }

    /// <summary>Builds the dismissal writer for an administrator.</summary>
    /// <param name="context">The context it writes through.</param>
    /// <param name="user">The caller it acts for.</param>
    /// <returns>The writer.</returns>
    public static NotificationDismissalWriter Writer(NotificationsDbContext context, ICurrentUser user)
    {
        return new NotificationDismissalWriter(context, user, new FakeClock());
    }

    /// <summary>Builds the journal over a recording writer.</summary>
    /// <param name="audit">Where entries are recorded.</param>
    /// <param name="user">The caller entries are attributed to.</param>
    /// <returns>The journal.</returns>
    public static NotificationsAuditJournal Journal(RecordingAuditWriter audit, ICurrentUser user)
    {
        return new NotificationsAuditJournal(audit, user);
    }

    /// <summary>Builds the resolver over stub localization where each key reads as itself.</summary>
    /// <param name="sentences">The error sentences the panel knows, code to sentence.</param>
    /// <returns>The resolver.</returns>
    public static NotificationTextResolver Resolver(Dictionary<string, string>? sentences = null)
    {
        return new NotificationTextResolver(
            new StubStringLocalizer<Resources.NotificationMessages>(),
            new StubErrorTexts(sentences ?? []));
    }
}
