using Maran.Modules.Notifications.Commands.DismissAllNotifications;
using Maran.Modules.Notifications.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Notifications.Tests.Commands.DismissAllNotifications;

/// <summary>Dismissing everything acts for the caller only.</summary>
public sealed class DismissAllNotificationsCommandHandlerTests
{
    /// <summary>Every entry the caller had not dismissed is dismissed, once, and journalled with the count.</summary>
    [Fact]
    public async Task Every_active_entry_is_dismissed_for_the_caller_and_journalled_with_the_count()
    {
        var admin = FixedCurrentUser.Administrator(Guid.NewGuid());
        var audit = new RecordingAuditWriter();
        await using var context = NotificationsTestContext.Create();
        await FeedWorld.SeedAsync(context, "a");
        await FeedWorld.SeedAsync(context, "b");
        var handler = new DismissAllNotificationsCommandHandler(
            FeedWorld.Writer(context, admin), FeedWorld.Journal(audit, admin));

        await handler.HandleAsync(new DismissAllNotificationsCommand(), CancellationToken.None);
        await handler.HandleAsync(new DismissAllNotificationsCommand(), CancellationToken.None);

        Assert.Equal(2, await context.NotificationDismissals.CountAsync());
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("2", entry.Subject);
    }

    /// <summary>Another administrator's rows are untouched: the second administrator still has every entry.</summary>
    [Fact]
    public async Task Dismissing_all_never_writes_for_another_administrator()
    {
        var first = FixedCurrentUser.Administrator(Guid.NewGuid());
        await using var context = NotificationsTestContext.Create();
        await FeedWorld.SeedAsync(context);

        await FeedWorld.Writer(context, first).DismissAsync(null, CancellationToken.None);

        var row = await context.NotificationDismissals.SingleAsync();
        Assert.Equal(((Maran.SharedKernel.Callers.AdministratorCaller)first.Caller).UserId, row.UserId);
    }
}
