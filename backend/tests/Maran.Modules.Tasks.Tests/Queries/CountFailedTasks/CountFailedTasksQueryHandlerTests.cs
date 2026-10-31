using Maran.Modules.Tasks.Queries.CountFailedTasks;
using Maran.Modules.Tasks.Tests.TestSupport;

namespace Maran.Modules.Tasks.Tests.Queries.CountFailedTasks;

/// <summary>How many failed tasks the attention figures report, for each kind of caller.</summary>
public sealed class CountFailedTasksQueryHandlerTests
{
    /// <summary>An administrator's count is of every failed task, and a customer's of their own visible ones.</summary>
    /// <remarks>
    /// One database, two principals: the administrator's count is the inverse control — a filter
    /// mutated to hide everything would still give the customer a believable zero.
    /// </remarks>
    [Fact]
    public async Task Each_caller_counts_the_failed_tasks_they_may_see()
    {
        var database = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        var ownFailed = TasksTestContext.Row(accountId: mine);
        ownFailed.Fail("AgentSystemFailure", TasksTestContext.Now);
        var otherFailed = TasksTestContext.Row(accountId: Guid.NewGuid());
        otherFailed.Fail("AgentSystemFailure", TasksTestContext.Now);
        var serverFailed = TasksTestContext.Row();
        serverFailed.Fail("AgentSystemFailure", TasksTestContext.Now);
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.AddRange(ownFailed, otherFailed, serverFailed, TasksTestContext.Row(accountId: mine));
            await seed.SaveChangesAsync();
        }

        await using var asAdmin = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        await using var asCustomer = TasksTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var adminCount = await new CountFailedTasksQueryHandler(asAdmin)
            .HandleAsync(new CountFailedTasksQuery(), CancellationToken.None);
        var customerCount = await new CountFailedTasksQueryHandler(asCustomer)
            .HandleAsync(new CountFailedTasksQuery(), CancellationToken.None);

        Assert.Equal(3, adminCount.Value);
        Assert.Equal(1, customerCount.Value);
    }
}
