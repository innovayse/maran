using Maran.Modules.Tasks.IntegrationEvents.Handlers;
using Maran.Modules.Tasks.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Tasks.Tests.IntegrationEvents.Handlers;

/// <summary>What an account deletion does to this module's rows: releases them, never deletes them.</summary>
public sealed class AccountDeletingHandlerTests
{
    /// <summary>Every task of the deleted account is released and kept, and no other account's is touched.</summary>
    /// <remarks>
    /// The surviving account's row is the inverse control: a handler that released everything would
    /// pass an assertion that only looked at the deleted account, and would hide every other
    /// customer's tasks from them.
    /// </remarks>
    [Fact]
    public async Task Every_task_of_the_deleted_account_is_released_and_kept_and_no_other_accounts_is_touched()
    {
        var database = Guid.NewGuid().ToString();
        var deleted = Guid.NewGuid();
        var surviving = Guid.NewGuid();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "a", accountId: deleted));
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "b", accountId: deleted));
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "c", accountId: surviving));
            await seed.SaveChangesAsync();
        }

        // A customer's principal on purpose: the handler must find the rows whoever is signed in.
        await using (var context = TasksTestContext.Create(FakeCurrentUser.Customer(), database))
        {
            await new AccountDeletingHandler(context).HandleAsync(
                new AccountDeleting(deleted, "acme"), CancellationToken.None);
        }

        await using var reader = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var rows = await reader.PanelTasks.ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(rows, row => { return row.AccountId == deleted; });
        Assert.Equal(surviving, Assert.Single(rows, row => { return row.AccountId is not null; }).AccountId);
    }

    /// <summary>An account with no tasks is handled without a failure.</summary>
    [Fact]
    public async Task An_account_with_no_tasks_is_handled_without_a_failure()
    {
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin());

        var thrown = await Record.ExceptionAsync(async () =>
        {
            await new AccountDeletingHandler(context).HandleAsync(
                new AccountDeleting(Guid.NewGuid(), "acme"), CancellationToken.None);
        });

        Assert.Null(thrown);
    }
}
