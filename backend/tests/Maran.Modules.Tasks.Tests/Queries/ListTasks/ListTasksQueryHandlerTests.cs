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
        var handler = new ListTasksQueryHandler(context, admin);

        var result = await handler.HandleAsync(new ListTasksQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["newer.example", "older.example"], result.Value.Select(task =>
        {
            return task.Subject;
        }));
    }

    /// <summary>A customer listing tasks is answered not found rather than an empty list.</summary>
    /// <remarks>
    /// The whole surface is administrator-only, so the honest answer to a customer is that it is not
    /// there. An empty 200 would say something different and slightly worse: that there is a tasks
    /// feed here and they simply have nothing in it.
    /// </remarks>
    [Fact]
    public async Task A_customer_listing_tasks_is_answered_not_found_rather_than_an_empty_list()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row());
            await seed.SaveChangesAsync();
        }

        var customer = FakeCurrentUser.Customer();
        await using var context = TasksTestContext.Create(customer, database);
        var handler = new ListTasksQueryHandler(context, customer);

        var result = await handler.HandleAsync(new ListTasksQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("TaskNotFound", result.Error!.Code);
    }
}
