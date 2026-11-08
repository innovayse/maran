using Maran.Modules.Notifications.Commands.DismissNotification;
using Maran.Modules.Notifications.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Notifications.Tests.Commands.DismissNotification;

/// <summary>Dismissal is per administrator, idempotent, and journalled only when it changed something.</summary>
public sealed class DismissNotificationCommandHandlerTests
{
    /// <summary>Dismissing writes one row for the caller and one journal entry.</summary>
    [Fact]
    public async Task Dismissing_writes_one_dismissal_for_the_caller_and_journals_it()
    {
        var database = Guid.NewGuid().ToString();
        var admin = FixedCurrentUser.Administrator(Guid.NewGuid());
        var audit = new RecordingAuditWriter();
        await using var context = NotificationsTestContext.Create(database);
        var entry = await FeedWorld.SeedAsync(context);

        var result = await Handler(context, admin, audit).HandleAsync(
            new DismissNotificationCommand(entry.Id, "203.0.113.7", "curl/8"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(await context.NotificationDismissals.ToListAsync());
        var journal = Assert.Single(audit.Entries);
        Assert.Equal(OutgoingMailAuditActions.NotificationsDismissed, journal.Action);
        Assert.True(journal.Succeeded);
    }

    /// <summary>One administrator's dismissal leaves the entry active for another (the cross-administrator control).</summary>
    [Fact]
    public async Task One_administrators_dismissal_leaves_the_entry_for_the_other()
    {
        var database = Guid.NewGuid().ToString();
        var first = FixedCurrentUser.Administrator(Guid.NewGuid());
        var second = FixedCurrentUser.Administrator(Guid.NewGuid());
        await using var context = NotificationsTestContext.Create(database);
        var entry = await FeedWorld.SeedAsync(context);

        await Handler(context, first, new RecordingAuditWriter()).HandleAsync(
            new DismissNotificationCommand(entry.Id), CancellationToken.None);

        var rows = await context.NotificationDismissals.ToListAsync();
        var only = Assert.Single(rows);
        Assert.Equal(((Maran.SharedKernel.Callers.AdministratorCaller)first.Caller).UserId, only.UserId);
        Assert.DoesNotContain(rows, row => { return row.UserId == ((Maran.SharedKernel.Callers.AdministratorCaller)second.Caller).UserId; });
    }

    /// <summary>Dismissing twice, or an id that does not exist, succeeds, writes nothing more and journals nothing more.</summary>
    [Fact]
    public async Task Dismissing_twice_or_an_unknown_id_writes_nothing_more()
    {
        var admin = FixedCurrentUser.Administrator(Guid.NewGuid());
        var audit = new RecordingAuditWriter();
        await using var context = NotificationsTestContext.Create();
        var entry = await FeedWorld.SeedAsync(context);
        var handler = Handler(context, admin, audit);

        await handler.HandleAsync(new DismissNotificationCommand(entry.Id), CancellationToken.None);
        var again = await handler.HandleAsync(new DismissNotificationCommand(entry.Id), CancellationToken.None);
        var unknown = await handler.HandleAsync(new DismissNotificationCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.True(unknown.IsSuccess);
        Assert.Single(await context.NotificationDismissals.ToListAsync());
        Assert.Single(audit.Entries);
    }

    /// <summary>A caller who is not an administrator dismisses nothing.</summary>
    [Fact]
    public async Task A_caller_who_is_not_an_administrator_dismisses_nothing()
    {
        await using var context = NotificationsTestContext.Create();
        var entry = await FeedWorld.SeedAsync(context);

        await Handler(context, FixedCurrentUser.Customer(), new RecordingAuditWriter()).HandleAsync(
            new DismissNotificationCommand(entry.Id), CancellationToken.None);

        Assert.Empty(await context.NotificationDismissals.ToListAsync());
    }

    /// <summary>Builds the handler for a caller.</summary>
    /// <param name="context">The context.</param>
    /// <param name="user">The caller.</param>
    /// <param name="audit">Where the journal writes.</param>
    /// <returns>The handler.</returns>
    private static DismissNotificationCommandHandler Handler(
        Persistence.NotificationsDbContext context,
        FixedCurrentUser user,
        RecordingAuditWriter audit)
    {
        return new DismissNotificationCommandHandler(FeedWorld.Writer(context, user), FeedWorld.Journal(audit, user));
    }
}
