using Maran.Modules.Notifications.Jobs;
using Maran.Modules.Notifications.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Notifications.Tests.Jobs;

/// <summary>Retention keeps the feed bounded by age and by count.</summary>
public sealed class NotificationRetentionHandlerTests
{
    /// <summary>Entries older than the window go; entries inside it stay.</summary>
    [Fact]
    public async Task Entries_older_than_the_window_are_deleted_and_newer_ones_kept()
    {
        var clock = new FakeClock();
        await using var context = NotificationsTestContext.Create();
        await FeedWorld.SeedAsync(context, "old", clock.UtcNow - NotificationRetentionHandler.RetentionWindow - TimeSpan.FromDays(1));
        await FeedWorld.SeedAsync(context, "recent", clock.UtcNow - TimeSpan.FromDays(1));

        var purged = await Handler(context, clock).HandleAsync(new NotificationRetentionRequested(), CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.Equal("recent", (await context.PanelNotifications.SingleAsync()).Subject);
    }

    /// <summary>Beyond the kept count the oldest go, even inside the window.</summary>
    [Fact]
    public async Task Entries_beyond_the_kept_count_are_deleted_oldest_first()
    {
        var clock = new FakeClock();
        await using var context = NotificationsTestContext.Create();
        for (var index = 0; index < NotificationRetentionHandler.MaxKept + 3; index++)
        {
            await FeedWorld.SeedAsync(context, "n" + index, clock.UtcNow.AddMinutes(-index));
        }

        var purged = await Handler(context, clock).HandleAsync(new NotificationRetentionRequested(), CancellationToken.None);

        Assert.Equal(3, purged);
        Assert.Equal(NotificationRetentionHandler.MaxKept, await context.PanelNotifications.CountAsync());
        Assert.DoesNotContain(
            await context.PanelNotifications.Select(n => n.Subject).ToListAsync(),
            subject => { return subject == "n" + (NotificationRetentionHandler.MaxKept + 2); });
    }

    /// <summary>A table within both bounds is left alone.</summary>
    [Fact]
    public async Task A_table_within_both_bounds_is_left_alone()
    {
        var clock = new FakeClock();
        await using var context = NotificationsTestContext.Create();
        await FeedWorld.SeedAsync(context, "fresh", clock.UtcNow);

        var purged = await Handler(context, clock).HandleAsync(new NotificationRetentionRequested(), CancellationToken.None);

        Assert.Equal(0, purged);
    }

    /// <summary>Builds the handler.</summary>
    /// <param name="context">The context.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The handler.</returns>
    private static NotificationRetentionHandler Handler(Persistence.NotificationsDbContext context, FakeClock clock)
    {
        return new NotificationRetentionHandler(context, clock, NullLogger<NotificationRetentionHandler>.Instance);
    }
}
