using Maran.Modules.Notifications.IntegrationEvents.Handlers;
using Maran.Modules.Notifications.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Notifications.Tests.IntegrationEvents.Handlers;

/// <summary>The feed's only writer: a raised notification becomes one stored, bounded entry.</summary>
public sealed class PanelNotificationRaisedHandlerTests
{
    /// <summary>A raised notification is stored with its source, code, severity, subject, values and link.</summary>
    [Fact]
    public async Task A_raised_notification_is_stored()
    {
        var database = Guid.NewGuid().ToString();
        await using var context = NotificationsTestContext.Create(database);

        await new PanelNotificationRaisedHandler(context).HandleAsync(
            new PanelNotificationRaised(
                NotificationSource.Task,
                "SiteNotFound",
                NotificationSeverity.Warning,
                "example.com",
                ["one", "two"],
                NotificationDeepLinks.Tasks,
                FeedWorld.Now),
            CancellationToken.None);

        await using var reader = NotificationsTestContext.Create(database);
        var stored = await reader.PanelNotifications.SingleAsync();
        Assert.Equal(NotificationSource.Task, stored.Source);
        Assert.Equal("SiteNotFound", stored.Code);
        Assert.Equal("example.com", stored.Subject);
        Assert.Equal(["one", "two"], stored.Arguments.Values);
        Assert.Equal(NotificationDeepLinks.Tasks, stored.DeepLink);
        Assert.Equal(FeedWorld.Now, stored.OccurredAt);
    }

    /// <summary>A deep link outside the closed set is dropped, not stored.</summary>
    [Fact]
    public async Task A_deep_link_outside_the_closed_set_is_dropped()
    {
        await using var context = NotificationsTestContext.Create();

        await new PanelNotificationRaisedHandler(context).HandleAsync(
            new PanelNotificationRaised(
                NotificationSource.Alert, "DiskUsage", NotificationSeverity.Critical, "/", [], "https://evil.example/", FeedWorld.Now),
            CancellationToken.None);

        Assert.Null((await context.PanelNotifications.SingleAsync()).DeepLink);
    }
}
