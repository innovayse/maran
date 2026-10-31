using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Models;
using Maran.Modules.Tasks.Services;
using Maran.Modules.Tasks.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Tasks.Tests.Services;

/// <summary>
/// What the shell's one running-tasks stream sends: every running task the subscriber may see, the
/// running set, a frame for each change, and nothing about a task the subscriber may not see.
/// </summary>
public sealed class RunningTasksStreamServiceTests
{
    /// <summary>The bound on every stream read, so a missing frame fails rather than hangs.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The opening frames carry every running task, then the set; a finished task is sent as it ended
    /// and leaves the set; a finished task never opens the stream at all.
    /// </summary>
    [Fact]
    public async Task One_reader_carries_every_running_task_then_the_set_and_follows_a_finish()
    {
        var database = Guid.NewGuid().ToString();
        var first = TasksTestContext.Row(subject: "a.example.com");
        var second = TasksTestContext.Row(subject: "b.example.com");
        var done = TasksTestContext.Row(subject: "done.example.com");
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            done.Complete(TasksTestContext.Now);
            seed.PanelTasks.AddRange(first, second, done);
            await seed.SaveChangesAsync();
        }

        var subscriber = FakeCurrentUser.Admin();
        await using var context = TasksTestContext.Create(subscriber, database);
        var service = new RunningTasksStreamService(
            context,
            TasksTestContext.StreamOptions(),
            TasksTestContext.KindNames(),
            TasksTestContext.FailureMessages(),
            subscriber);
        using var watching = new CancellationTokenSource(ReadTimeout);
        await using var frames = service.ReadAsync(watching.Token).GetAsyncEnumerator(watching.Token);

        var opening = await ReadUntilSetAsync(frames);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), IdsOf(opening.Tasks).Order());
        Assert.Equal(new[] { first.Id, second.Id }.Order(), opening.Set.Order());

        await using (var finish = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            var row = await finish.PanelTasks.SingleAsync(candidate => candidate.Id == first.Id);
            row.Complete(TasksTestContext.Now);
            await finish.SaveChangesAsync();
        }

        var after = await ReadUntilSetAsync(frames);
        var finished = Assert.Single(after.Tasks);
        Assert.Equal(first.Id, finished.Id);
        Assert.Equal(PanelTaskStatus.Completed, finished.Status);
        Assert.Equal([second.Id], after.Set);

        await watching.CancelAsync();
    }

    /// <summary>A customer's reader carries their own running task and not another tenant's or an administrator-only one.</summary>
    [Fact]
    public async Task A_customers_reader_carries_only_the_running_tasks_they_may_see()
    {
        var database = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid();
        var own = TasksTestContext.Row(accountId: accountId);
        var otherTenant = TasksTestContext.Row(accountId: Guid.NewGuid());
        var administratorOnly = TasksTestContext.Row(
            accountId: accountId, audience: TaskAudience.AdministratorOnly);
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            own.Report(10, "operator-only line");
            seed.PanelTasks.AddRange(own, otherTenant, administratorOnly);
            await seed.SaveChangesAsync();
        }

        var subscriber = FakeCurrentUser.Customer(accountId);
        await using var context = TasksTestContext.Create(subscriber, database);
        var service = new RunningTasksStreamService(
            context,
            TasksTestContext.StreamOptions(),
            TasksTestContext.KindNames(),
            TasksTestContext.FailureMessages(),
            subscriber);
        using var watching = new CancellationTokenSource(ReadTimeout);
        await using var frames = service.ReadAsync(watching.Token).GetAsyncEnumerator(watching.Token);

        var opening = await ReadUntilSetAsync(frames);

        var seen = Assert.Single(opening.Tasks);
        Assert.Equal(own.Id, seen.Id);
        Assert.Equal(string.Empty, seen.Log);
        Assert.Equal([own.Id], opening.Set);

        await watching.CancelAsync();
    }

    /// <summary>Reads the ids of the tasks a reader sent.</summary>
    /// <param name="tasks">The tasks.</param>
    /// <returns>Their ids, in the order sent.</returns>
    private static List<Guid> IdsOf(List<PanelTaskDto> tasks)
    {
        var ids = new List<Guid>();
        foreach (var task in tasks)
        {
            ids.Add(task.Id);
        }

        return ids;
    }

    /// <summary>Reads task frames until a running set arrives.</summary>
    /// <param name="frames">The reader's frames.</param>
    /// <returns>The tasks read and the set that closed them.</returns>
    private static async Task<(List<PanelTaskDto> Tasks, IReadOnlyList<Guid> Set)> ReadUntilSetAsync(
        IAsyncEnumerator<RunningTasksFrame> frames)
    {
        var tasks = new List<PanelTaskDto>();
        while (true)
        {
            Assert.True(await frames.MoveNextAsync());
            if (frames.Current.RunningIds is { } set)
            {
                return (tasks, set);
            }

            tasks.Add(Assert.IsType<PanelTaskDto>(frames.Current.Snapshot));
        }
    }
}
