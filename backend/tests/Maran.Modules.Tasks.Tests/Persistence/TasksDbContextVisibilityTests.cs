using Maran.Modules.Tasks.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Tasks.Tests.Persistence;

/// <summary>
/// The module's tenant query filter, tested where it lives rather than only through the
/// handlers that rely on it.
/// </summary>
/// <remarks>
/// Two contexts, two principals, ONE in-memory database: the only thing separating the rows is the
/// filter under test. A handler asserting the same thing could pass for a reason of its own, and the
/// filter is the reason a handler that forgot a clause still could not leak.
/// </remarks>
public sealed class TasksDbContextVisibilityTests
{
    /// <summary>A customer reading a tasks table holding no row of their account sees nothing at all.</summary>
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

    /// <summary>
    /// A customer reading the tasks table sees only the rows of their own account, never another
    /// tenant's and never one that belongs to no account.
    /// </summary>
    [Fact]
    public async Task A_customer_reading_the_tasks_table_sees_only_the_rows_of_their_own_account()
    {
        var database = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid();
        var own = TasksTestContext.Row(subject: "own.example", accountId: accountId);
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(own);
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "other.example", accountId: Guid.NewGuid()));
            seed.PanelTasks.Add(TasksTestContext.Row(subject: "server.example"));
            await seed.SaveChangesAsync();
        }

        await using var customer = TasksTestContext.Create(FakeCurrentUser.Customer(accountId), database);

        Assert.Equal(own.Id, Assert.Single(await customer.PanelTasks.ToListAsync()).Id);
    }

    /// <summary>A principal with no account sees no account-less row unless it is an administrator.</summary>
    /// <remarks>
    /// The filter's <c>AccountId != null</c> half, held up on its own: a non-administrator whose
    /// token names no account must not match the rows that belong to no account by comparing null
    /// with null.
    /// </remarks>
    [Fact]
    public async Task A_principal_with_no_account_sees_no_account_less_row_unless_it_is_an_administrator()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row());
            await seed.SaveChangesAsync();
        }

        var accountless = new FakeCurrentUser(Guid.NewGuid(), accountId: null, isAdmin: false);
        await using var context = TasksTestContext.Create(accountless, database);

        Assert.Empty(await context.PanelTasks.ToListAsync());
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
            seed.PanelTasks.Add(TasksTestContext.Row(kind: "BackupCreate", subject: "acme", accountId: Guid.NewGuid()));
            await seed.SaveChangesAsync();
        }

        await using var admin = TasksTestContext.Create(FakeCurrentUser.Admin(), database);

        Assert.Equal(3, (await admin.PanelTasks.ToListAsync()).Count);
    }

    /// <summary>A customer never sees an administrator-only task about their own account.</summary>
    /// <remarks>The account matches; only the audience keeps the row out, so this holds that half up alone.</remarks>
    [Fact]
    public async Task A_customer_never_sees_an_administrator_only_task_about_their_own_account()
    {
        var database = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row(
                kind: TaskKinds.AccountSuspension, accountId: accountId, audience: TaskAudience.AdministratorOnly));
            await seed.SaveChangesAsync();
        }

        await using var customer = TasksTestContext.Create(FakeCurrentUser.Customer(accountId), database);

        Assert.Empty(await customer.PanelTasks.ToListAsync());
    }

    /// <summary>An administrator finds the suspension and deletion tasks of an account by its id.</summary>
    [Fact]
    public async Task An_administrator_finds_the_suspension_and_deletion_tasks_of_an_account_by_its_id()
    {
        var database = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(TasksTestContext.Row(
                kind: TaskKinds.AccountSuspension, accountId: accountId, audience: TaskAudience.AdministratorOnly));
            seed.PanelTasks.Add(TasksTestContext.Row(
                kind: TaskKinds.AccountDeletion, accountId: accountId, audience: TaskAudience.AdministratorOnly));
            seed.PanelTasks.Add(TasksTestContext.Row(
                kind: TaskKinds.AccountDeletion, accountId: Guid.NewGuid(), audience: TaskAudience.AdministratorOnly));
            await seed.SaveChangesAsync();
        }

        await using var admin = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var kinds = await admin.PanelTasks
            .Where(task => task.AccountId == accountId)
            .Select(task => task.Kind)
            .OrderBy(kind => kind)
            .ToListAsync();

        Assert.Equal([TaskKinds.AccountDeletion, TaskKinds.AccountSuspension], kinds);
    }
}
