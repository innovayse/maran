using Maran.Modules.Tasks.Queries.GetTask;
using Maran.Modules.Tasks.Tests.TestSupport;

namespace Maran.Modules.Tasks.Tests.Queries.GetTask;

/// <summary>Who may read one task, and what the answer looks like when they may not.</summary>
public sealed class GetTaskQueryHandlerTests
{
    /// <summary>An administrator reading a task gets every column of it.</summary>
    [Fact]
    public async Task An_administrator_reading_a_task_gets_every_column_of_it()
    {
        var database = Guid.NewGuid().ToString();
        var task = TasksTestContext.Row(kind: "AccountDeletion", subject: "alice");
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            task.Report(45, "cascading");
            seed.PanelTasks.Add(task);
            await seed.SaveChangesAsync();
        }

        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var handler = new GetTaskQueryHandler(context);

        var result = await handler.HandleAsync(new GetTaskQuery(task.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("AccountDeletion", result.Value.Kind);
        Assert.Equal("alice", result.Value.Subject);
        Assert.Equal(45, result.Value.Percent);
        Assert.Equal("cascading", result.Value.Log);
    }

    /// <summary>A customer reading an administrators task is answered not found rather than forbidden.</summary>
    /// <remarks>
    /// 404 and never 403: a task names a domain or an account name, so confirming that one exists is
    /// itself the disclosure (spec §8, rules/testing.md item 3). The row genuinely is not in the
    /// result set — the context's query filter removed it — so nothing here had to remember to.
    /// </remarks>
    [Fact]
    public async Task A_customer_reading_an_administrators_task_is_answered_not_found_rather_than_forbidden()
    {
        var database = Guid.NewGuid().ToString();
        var task = TasksTestContext.Row();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(task);
            await seed.SaveChangesAsync();
        }

        await using var context = TasksTestContext.Create(FakeCurrentUser.Customer(), database);
        var handler = new GetTaskQueryHandler(context);

        var result = await handler.HandleAsync(new GetTaskQuery(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("TaskNotFound", result.Error!.Code);
    }

    /// <summary>A task that does not exist is answered not found.</summary>
    [Fact]
    public async Task A_task_that_does_not_exist_is_answered_not_found()
    {
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin());
        var handler = new GetTaskQueryHandler(context);

        var result = await handler.HandleAsync(new GetTaskQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("TaskNotFound", result.Error!.Code);
    }
}
