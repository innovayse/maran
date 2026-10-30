using Maran.Modules.Tasks.Queries.ListTasks;
using Maran.Modules.Tasks.Tests.TestSupport;

namespace Maran.Modules.Tasks.Tests.Queries.ListTasks;

/// <summary>Who the tasks listing answers, and with what.</summary>
public sealed class ListTasksQueryHandlerTests
{
    /// <summary>An administrator sees the panels tasks newest first.</summary>
    [Fact]
    public async Task An_administrator_sees_the_panels_tasks_newest_first()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row(
                subject: "older.example", startedAt: TasksTestContext.Now.AddMinutes(-30)));
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "newer.example"));
            await seed.SaveChangesAsync();
        }

        var admin = FakeCurrentUser.Admin();
        await using var context = TasksTestContext.Create(admin, database);
        var handler = new ListTasksQueryHandler(context, admin, TasksTestContext.KindNames());

        var result = await handler.HandleAsync(new ListTasksQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["newer.example", "older.example"], result.Value.Select(task =>
        {
            return task.Subject;
        }));
    }

    /// <summary>A customer listing tasks gets only their own account's tasks, each with an empty log.</summary>
    /// <remarks>
    /// Three rows, one per kind of owner: the customer's own, another tenant's, and one that belongs
    /// to no account. Only the first may come back, and its log must not — the log is operator
    /// material (rules/security.md item 8).
    /// </remarks>
    [Fact]
    public async Task A_customer_listing_tasks_gets_only_their_own_accounts_tasks_each_with_an_empty_log()
    {
        var database = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid();
        var own = TasksTestContext.Row(subject: "own.example", accountId: accountId);
        own.Report(50, "tool output");
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(own);
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "other.example", accountId: Guid.NewGuid()));
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "server.example"));
            await seed.SaveChangesAsync();
        }

        var customer = FakeCurrentUser.Customer(accountId);
        await using var context = TasksTestContext.Create(customer, database);
        var handler = new ListTasksQueryHandler(context, customer, TasksTestContext.KindNames());

        var result = await handler.HandleAsync(new ListTasksQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var task = Assert.Single(result.Value);
        Assert.Equal(own.Id, task.Id);
        Assert.False(task.LogIncluded);
        Assert.Equal(string.Empty, task.Log);
    }

    /// <summary>A customer with no tasks of their own gets an empty list rather than not found.</summary>
    [Fact]
    public async Task A_customer_with_no_tasks_of_their_own_gets_an_empty_list_rather_than_not_found()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row());
            await seed.SaveChangesAsync();
        }

        var customer = FakeCurrentUser.Customer();
        await using var context = TasksTestContext.Create(customer, database);
        var handler = new ListTasksQueryHandler(context, customer, TasksTestContext.KindNames());

        var result = await handler.HandleAsync(new ListTasksQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    /// <summary>An administrator listing tasks is sent each task's log.</summary>
    /// <remarks>The inverse control for the redaction above: a mapper that dropped every log would pass it.</remarks>
    [Fact]
    public async Task An_administrator_listing_tasks_is_sent_each_tasks_log()
    {
        var database = Guid.NewGuid().ToString();
        var task = TasksTestContext.Row(accountId: Guid.NewGuid());
        task.Report(50, "tool output");
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(task);
            await seed.SaveChangesAsync();
        }

        var admin = FakeCurrentUser.Admin();
        await using var context = TasksTestContext.Create(admin, database);
        var handler = new ListTasksQueryHandler(context, admin, TasksTestContext.KindNames());

        var result = await handler.HandleAsync(new ListTasksQuery(), CancellationToken.None);

        var listed = Assert.Single(result.Value);
        Assert.True(listed.LogIncluded);
        Assert.Equal("tool output", listed.Log);
    }
}
