using Maran.Modules.Notifications.Queries.ListNotifications;
using Maran.Modules.Notifications.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Notifications.Tests.Queries.ListNotifications;

/// <summary>The feed lists what the caller has not dismissed, newest first, and nothing to a non-administrator.</summary>
public sealed class ListNotificationsQueryHandlerTests
{
    /// <summary>Entries come newest first.</summary>
    [Fact]
    public async Task Entries_come_newest_first()
    {
        var admin = FixedCurrentUser.Administrator(Guid.NewGuid());
        await using var context = NotificationsTestContext.Create();
        await FeedWorld.SeedAsync(context, "old", FeedWorld.Now.AddHours(-2));
        await FeedWorld.SeedAsync(context, "new", FeedWorld.Now);

        var page = (await Handler(context, admin).HandleAsync(new ListNotificationsQuery(), CancellationToken.None)).Value!;

        Assert.Equal(2, page.Total);
        Assert.StartsWith("FeedTaskFailed|new", page.Items[0].Title, StringComparison.Ordinal);
    }

    /// <summary>A dismissed entry is gone for the dismisser and still listed for the other administrator.</summary>
    [Fact]
    public async Task A_dismissed_entry_is_hidden_from_the_dismisser_only()
    {
        var first = FixedCurrentUser.Administrator(Guid.NewGuid());
        var second = FixedCurrentUser.Administrator(Guid.NewGuid());
        await using var context = NotificationsTestContext.Create();
        var entry = await FeedWorld.SeedAsync(context);
        await FeedWorld.Writer(context, first).DismissAsync(entry.Id, CancellationToken.None);

        var forFirst = (await Handler(context, first).HandleAsync(new ListNotificationsQuery(), CancellationToken.None)).Value!;
        var forSecond = (await Handler(context, second).HandleAsync(new ListNotificationsQuery(), CancellationToken.None)).Value!;

        Assert.Equal(0, forFirst.Total);
        Assert.Equal(1, forSecond.Total);
    }

    /// <summary>The source filter narrows, and its inverse control keeps the matching source.</summary>
    [Fact]
    public async Task The_source_filter_narrows_to_that_source()
    {
        var admin = FixedCurrentUser.Administrator(Guid.NewGuid());
        await using var context = NotificationsTestContext.Create();
        await FeedWorld.SeedAsync(context, "task", source: NotificationSource.Task);
        await FeedWorld.SeedAsync(context, "alert", source: NotificationSource.Alert);

        var alerts = (await Handler(context, admin).HandleAsync(
            new ListNotificationsQuery(Source: NotificationSource.Alert), CancellationToken.None)).Value!;
        var backups = (await Handler(context, admin).HandleAsync(
            new ListNotificationsQuery(Source: NotificationSource.Backup), CancellationToken.None)).Value!;

        Assert.Equal(NotificationSource.Alert, Assert.Single(alerts.Items).Source);
        Assert.Equal(0, backups.Total);
    }

    /// <summary>A customer is shown nothing even though entries exist.</summary>
    [Fact]
    public async Task A_customer_is_shown_nothing()
    {
        await using var context = NotificationsTestContext.Create();
        await FeedWorld.SeedAsync(context);

        var page = (await Handler(context, FixedCurrentUser.Customer()).HandleAsync(
            new ListNotificationsQuery(), CancellationToken.None)).Value!;

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
    }

    /// <summary>Builds the handler for a caller.</summary>
    /// <param name="context">The context.</param>
    /// <param name="user">The caller.</param>
    /// <returns>The handler.</returns>
    private static ListNotificationsQueryHandler Handler(Persistence.NotificationsDbContext context, FixedCurrentUser user)
    {
        return new ListNotificationsQueryHandler(context, user, FeedWorld.Resolver());
    }
}
