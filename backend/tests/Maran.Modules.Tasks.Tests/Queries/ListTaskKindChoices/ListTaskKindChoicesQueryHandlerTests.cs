using Maran.Modules.Tasks.Queries.ListTaskKindChoices;
using Maran.Modules.Tasks.Tests.TestSupport;

namespace Maran.Modules.Tasks.Tests.Queries.ListTaskKindChoices;

/// <summary>What the tasks list's kind filter is offered: the kinds among the rows the caller may see, once each.</summary>
public sealed class ListTaskKindChoicesQueryHandlerTests
{
    /// <summary>
    /// A customer is offered only the kinds among their own visible tasks, while an administrator is
    /// offered every kind once — so the choice list itself discloses nothing a customer could not open.
    /// </summary>
    [Fact]
    public async Task Each_caller_is_offered_the_kinds_among_the_tasks_they_may_see()
    {
        var database = Guid.NewGuid().ToString();
        var mine = Guid.NewGuid();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.AddRange(
                TasksTestContext.Row(kind: "BackupCreate", accountId: mine),
                TasksTestContext.Row(kind: "BackupCreate", accountId: mine),
                TasksTestContext.Row(kind: "BackupRestore", accountId: Guid.NewGuid()),
                TasksTestContext.Row(kind: "PhpVersionInstall"));
            await seed.SaveChangesAsync();
        }

        await using var asAdmin = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        await using var asCustomer = TasksTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var adminKinds = await new ListTaskKindChoicesQueryHandler(asAdmin, TasksTestContext.KindNames())
            .HandleAsync(new ListTaskKindChoicesQuery(), CancellationToken.None);
        var customerKinds = await new ListTaskKindChoicesQueryHandler(asCustomer, TasksTestContext.KindNames())
            .HandleAsync(new ListTaskKindChoicesQuery(), CancellationToken.None);

        Assert.Equal(
            ["BackupCreate", "BackupRestore", "PhpVersionInstall"],
            adminKinds.Value
                .Select(choice =>
                {
                    return choice.Kind;
                })
                .Order(StringComparer.Ordinal));
        var only = Assert.Single(customerKinds.Value);
        Assert.Equal("BackupCreate", only.Kind);
        Assert.False(string.IsNullOrEmpty(only.KindName));
    }
}
