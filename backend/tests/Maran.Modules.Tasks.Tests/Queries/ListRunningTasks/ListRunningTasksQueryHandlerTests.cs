using Maran.Modules.Tasks.Queries.ListRunningTasks;
using Maran.Modules.Tasks.Tests.TestSupport;

namespace Maran.Modules.Tasks.Tests.Queries.ListRunningTasks;

/// <summary>What the shell's badge is given: every running task the caller may see, and no finished one.</summary>
public sealed class ListRunningTasksQueryHandlerTests
{
    /// <summary>Only running tasks are listed, newest first, however many finished tasks surround them.</summary>
    [Fact]
    public async Task Only_running_tasks_are_listed_newest_first()
    {
        var database = Guid.NewGuid().ToString();
        var completed = TasksTestContext.Row(subject: "completed.example");
        completed.Complete(TasksTestContext.Now);
        var failed = TasksTestContext.Row(subject: "failed.example");
        failed.Fail("AgentSystemFailure", TasksTestContext.Now);
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.AddRange(
                completed,
                failed,
                TasksTestContext.Row(subject: "older.example", startedAt: TasksTestContext.Now.AddMinutes(-5)),
                TasksTestContext.Row(subject: "newer.example"));
            await seed.SaveChangesAsync();
        }

        var admin = FakeCurrentUser.Admin();
        await using var context = TasksTestContext.Create(admin, database);
        var result = await new ListRunningTasksQueryHandler(
            context, admin, TasksTestContext.KindNames(), TasksTestContext.FailureMessages())
            .HandleAsync(new ListRunningTasksQuery(), CancellationToken.None);

        Assert.Equal(["newer.example", "older.example"], result.Value.Select(task =>
        {
            return task.Subject;
        }));
    }

    /// <summary>A customer is given only their own running tasks, each with the log withheld.</summary>
    [Fact]
    public async Task A_customer_is_given_only_their_own_running_tasks_without_the_log()
    {
        var database = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        var own = TasksTestContext.Row(subject: "own.example", accountId: mine);
        own.Report(10, "tool output");
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.AddRange(
                own,
                TasksTestContext.Row(subject: "other.example", accountId: Guid.NewGuid()),
                TasksTestContext.Row(subject: "server"));
            await seed.SaveChangesAsync();
        }

        var customer = FakeCurrentUser.Customer(mine);
        await using var context = TasksTestContext.Create(customer, database);
        var result = await new ListRunningTasksQueryHandler(
            context, customer, TasksTestContext.KindNames(), TasksTestContext.FailureMessages())
            .HandleAsync(new ListRunningTasksQuery(), CancellationToken.None);

        var task = Assert.Single(result.Value);
        Assert.Equal(own.Id, task.Id);
        Assert.False(task.LogIncluded);
        Assert.Equal(string.Empty, task.Log);
    }
}
