using Maran.Modules.Tasks.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Tasks.Tests.Persistence;

/// <summary>
/// The module's administrator query filter, tested where it lives rather than only through the
/// handlers that rely on it.
/// </summary>
/// <remarks>
/// Two contexts, two principals, ONE in-memory database: the only thing separating the rows is the
/// filter under test. A handler asserting the same thing could pass for a reason of its own, and the
/// filter is the reason a handler that forgot a clause still could not leak.
/// </remarks>
public sealed class TasksDbContextVisibilityTests
{
    /// <summary>A customer reading the tasks table sees nothing at all.</summary>
    [Fact]
    public async Task A_customer_reading_the_tasks_table_sees_nothing_at_all()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row());
            seed.PanelTasks.Add(TasksTestContext.Row(kind: "AccountDeletion", subject: "alice"));
            await seed.SaveChangesAsync();
        }

        await using var customer = TasksTestContext.Create(FakeCurrentUser.Customer(), database);

        Assert.Empty(await customer.PanelTasks.ToListAsync());
    }

    /// <summary>An administrator reading the tasks table sees every row.</summary>
    [Fact]
    public async Task An_administrator_reading_the_tasks_table_sees_every_row()
    {
        // Without this the filter could be "nobody sees anything" and the test above would still
        // pass while the feature was entirely dead.
        var database = Guid.NewGuid().ToString();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row());
            seed.PanelTasks.Add(TasksTestContext.Row(kind: "AccountDeletion", subject: "alice"));
            await seed.SaveChangesAsync();
        }

        await using var admin = TasksTestContext.Create(FakeCurrentUser.Admin(), database);

        Assert.Equal(2, (await admin.PanelTasks.ToListAsync()).Count);
    }
}
