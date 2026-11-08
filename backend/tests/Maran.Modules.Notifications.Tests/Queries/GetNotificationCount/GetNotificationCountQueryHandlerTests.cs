using Maran.Modules.Notifications.Queries.GetNotificationCount;
using Maran.Modules.Notifications.Tests.TestSupport;

namespace Maran.Modules.Notifications.Tests.Queries.GetNotificationCount;

/// <summary>The badge figure: the caller's active entries, capped, and zero for a non-administrator.</summary>
public sealed class GetNotificationCountQueryHandlerTests
{
    /// <summary>The count is the caller's active entries and falls when they dismiss one.</summary>
    [Fact]
    public async Task The_count_is_the_callers_active_entries()
    {
        var admin = FixedCurrentUser.Administrator(Guid.NewGuid());
        await using var context = NotificationsTestContext.Create();
        var first = await FeedWorld.SeedAsync(context, "a");
        await FeedWorld.SeedAsync(context, "b");
        await FeedWorld.Writer(context, admin).DismissAsync(first.Id, CancellationToken.None);

        var count = (await new GetNotificationCountQueryHandler(context, admin)
            .HandleAsync(new GetNotificationCountQuery(), CancellationToken.None)).Value!;

        Assert.Equal(1, count.Count);
        Assert.False(count.Capped);
    }

    /// <summary>More than the cap reads as the cap with the flag set; exactly the cap is not flagged.</summary>
    [Fact]
    public async Task More_than_the_cap_reads_as_the_cap_with_the_flag()
    {
        var admin = FixedCurrentUser.Administrator(Guid.NewGuid());
        await using var context = NotificationsTestContext.Create();
        for (var index = 0; index < GetNotificationCountQueryHandler.Cap; index++)
        {
            await FeedWorld.SeedAsync(context, "n" + index);
        }

        var atCap = (await new GetNotificationCountQueryHandler(context, admin)
            .HandleAsync(new GetNotificationCountQuery(), CancellationToken.None)).Value!;
        await FeedWorld.SeedAsync(context, "one more");
        var over = (await new GetNotificationCountQueryHandler(context, admin)
            .HandleAsync(new GetNotificationCountQuery(), CancellationToken.None)).Value!;

        Assert.False(atCap.Capped);
        Assert.Equal(GetNotificationCountQueryHandler.Cap, over.Count);
        Assert.True(over.Capped);
    }

    /// <summary>A customer is counted zero.</summary>
    [Fact]
    public async Task A_customer_is_counted_zero()
    {
        await using var context = NotificationsTestContext.Create();
        await FeedWorld.SeedAsync(context);

        var count = (await new GetNotificationCountQueryHandler(context, FixedCurrentUser.Customer())
            .HandleAsync(new GetNotificationCountQuery(), CancellationToken.None)).Value!;

        Assert.Equal(0, count.Count);
    }
}
