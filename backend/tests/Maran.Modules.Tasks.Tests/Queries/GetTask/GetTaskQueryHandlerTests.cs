using Maran.Modules.Tasks.Queries.GetTask;
using Maran.Modules.Tasks.Tests.TestSupport;
using Maran.Sdk.Contracts;

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
        var handler = new GetTaskQueryHandler(context, TasksTestContext.KindNames());

        var result = await handler.HandleAsync(new GetTaskQuery(task.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("AccountDeletion", result.Value.Kind);
        Assert.Equal("Deleting an account", result.Value.KindDisplayName);
        Assert.Equal("alice", result.Value.Subject);
        Assert.Equal(45, result.Value.Percent);
        Assert.Equal("cascading", result.Value.Log);
    }

    /// <summary>Every shipped task kind is answered with a name, never with its machine constant.</summary>
    /// <remarks>
    /// The pinned defect: the tasks screen printed <c>BackupRestore</c> and <c>BackupCreate</c>
    /// verbatim because the wire carried nothing else. This walks every <c>TaskKinds</c> constant
    /// rather than the two that were noticed, so a kind added later without its resx triple fails
    /// here instead of reaching an operator as a constant. The assertion is on the VALUE — a name
    /// that is merely non-empty is satisfied by the constant itself.
    /// </remarks>
    [Theory]
    [InlineData(TaskKinds.CertificateIssue, "Issuing a certificate")]
    [InlineData(TaskKinds.CertificateRenewal, "Renewing a certificate")]
    [InlineData(TaskKinds.AccountDeletion, "Deleting an account")]
    [InlineData(TaskKinds.AccountSuspension, "Suspending an account")]
    [InlineData(TaskKinds.AccountResumption, "Reactivating an account")]
    [InlineData(TaskKinds.BackupCreate, "Taking a backup")]
    [InlineData(TaskKinds.BackupRestore, "Restoring a backup")]
    public async Task Every_shipped_task_kind_is_answered_with_a_name(string kind, string expected)
    {
        var database = Guid.NewGuid().ToString();
        var task = TasksTestContext.Row(kind: kind, subject: "alice");
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(task);
            await seed.SaveChangesAsync();
        }

        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var handler = new GetTaskQueryHandler(context, TasksTestContext.KindNames());

        var result = await handler.HandleAsync(new GetTaskQuery(task.Id), CancellationToken.None);

        Assert.Equal(expected, result.Value.KindDisplayName);
        Assert.NotEqual(kind, result.Value.KindDisplayName);
    }

    /// <summary>A kind this build has no name for falls back to the kind itself, not to a broken key.</summary>
    /// <remarks>
    /// <c>TaskKinds</c> is a set of string constants and not an enum precisely so a marketplace
    /// module can record a kind this assembly never knew about, so a miss is a real case and not a
    /// hypothetical. Without the fallback <see cref="Microsoft.Extensions.Localization.IStringLocalizer"/>
    /// answers a missing key with the KEY, and an operator would read <c>TaskKindWhatever</c> — worse
    /// than the raw constant this defect started from.
    /// </remarks>
    [Fact]
    public async Task A_kind_this_build_has_no_name_for_falls_back_to_the_kind_itself()
    {
        var database = Guid.NewGuid().ToString();
        var task = TasksTestContext.Row(kind: "MarketplaceWidgetRebuild", subject: "alice");
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(task);
            await seed.SaveChangesAsync();
        }

        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var handler = new GetTaskQueryHandler(context, TasksTestContext.KindNames());

        var result = await handler.HandleAsync(new GetTaskQuery(task.Id), CancellationToken.None);

        Assert.Equal("MarketplaceWidgetRebuild", result.Value.KindDisplayName);
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
        var handler = new GetTaskQueryHandler(context, TasksTestContext.KindNames());

        var result = await handler.HandleAsync(new GetTaskQuery(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("TaskNotFound", result.Error!.Code);
    }

    /// <summary>A task that does not exist is answered not found.</summary>
    [Fact]
    public async Task A_task_that_does_not_exist_is_answered_not_found()
    {
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin());
        var handler = new GetTaskQueryHandler(context, TasksTestContext.KindNames());

        var result = await handler.HandleAsync(new GetTaskQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("TaskNotFound", result.Error!.Code);
    }
}
