using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Models;
using Maran.Modules.Tasks.Services;
using Maran.Modules.Tasks.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Tasks.Tests.Services;

/// <summary>
/// What a watcher is sent: the task as it stands, one more frame each time it changes, exactly one
/// ending — and nothing at all for a caller this surface does not exist for.
/// </summary>
public sealed class TaskStreamServiceTests
{
    /// <summary>The bound on every stream read, so a failure to produce a frame fails rather than hangs.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>A watcher is sent the task as it stands and then a frame for each change.</summary>
    /// <remarks>
    /// The two-frame shape is the point. A test that reads ONE frame and stops has proved the
    /// connection opens and nothing about the stream: the second frame is what shows a change
    /// reaching a watcher who was already attached, which is the whole reason the endpoint exists.
    /// The cancel at the end is part of the assertion too — the reader must stop on the watcher's
    /// token rather than on an exception, because the operation it is watching carries on.
    /// </remarks>
    [Fact]
    public async Task A_watcher_is_sent_the_task_as_it_stands_and_then_a_frame_for_each_change()
    {
        var database = Guid.NewGuid().ToString();
        var task = TasksTestContext.Row();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            task.Report(20, "ordering");
            seed.PanelTasks.Add(task);
            await seed.SaveChangesAsync();
        }

        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var service = new TaskStreamService(context, TasksTestContext.StreamOptions());

        using var watching = new CancellationTokenSource(ReadTimeout);
        await using var frames = service.ReadAsync(task.Id, watching.Token).GetAsyncEnumerator(watching.Token);

        Assert.True(await frames.MoveNextAsync());
        var first = Assert.IsType<PanelTaskDto>(frames.Current.Snapshot);
        Assert.Equal(20, first.Percent);

        // The change a watcher who was already attached must be told about.
        await using (var progress = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            var row = await progress.PanelTasks.SingleAsync(candidate => candidate.Id == task.Id);
            row.Report(70, "installing");
            await progress.SaveChangesAsync();
        }

        Assert.True(await frames.MoveNextAsync());
        var second = Assert.IsType<PanelTaskDto>(frames.Current.Snapshot);
        Assert.Equal(70, second.Percent);
        Assert.Equal("ordering\ninstalling", second.Log);

        // The watcher closes the pane. The reader stops with it, cleanly: no exception escapes the
        // enumerator's disposal, and the operation being watched is untouched.
        await watching.CancelAsync();
        var thrown = await Record.ExceptionAsync(async () =>
        {
            await frames.DisposeAsync();
        });

        Assert.Null(thrown);
    }

    /// <summary>A task that finishes is followed by exactly one ending naming its status.</summary>
    [Fact]
    public async Task A_task_that_finishes_is_followed_by_exactly_one_ending_naming_its_status()
    {
        var database = Guid.NewGuid().ToString();
        var task = TasksTestContext.Row();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            task.Fail("SiteNotFound", TasksTestContext.Now.AddSeconds(4));
            seed.PanelTasks.Add(task);
            await seed.SaveChangesAsync();
        }

        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var service = new TaskStreamService(context, TasksTestContext.StreamOptions());

        using var watching = new CancellationTokenSource(ReadTimeout);
        var collected = new List<TaskFrame>();
        await foreach (var frame in service.ReadAsync(task.Id, watching.Token))
        {
            collected.Add(frame);
        }

        Assert.Equal(2, collected.Count);
        Assert.NotNull(collected[0].Snapshot);
        Assert.Equal(PanelTaskStatus.Failed, collected[1].EndStatus);
    }

    /// <summary>A watcher who may not see a task is answered not found rather than an empty stream.</summary>
    /// <remarks>
    /// Settled BEFORE any byte goes out, which is what makes an ordinary 404 possible at all: once
    /// the stream's headers have flushed the status line is fixed, and "not found" could only be
    /// expressed as a stream that opens and says nothing.
    /// </remarks>
    [Fact]
    public async Task A_watcher_who_may_not_see_a_task_is_answered_not_found_rather_than_an_empty_stream()
    {
        var database = Guid.NewGuid().ToString();
        var task = TasksTestContext.Row();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(task);
            await seed.SaveChangesAsync();
        }

        await using var context = TasksTestContext.Create(FakeCurrentUser.Customer(), database);
        var service = new TaskStreamService(context, TasksTestContext.StreamOptions());

        var result = await service.ResolveAsync(task.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("TaskNotFound", result.Error!.Code);
    }

    /// <summary>An administrator may watch a task that exists.</summary>
    [Fact]
    public async Task An_administrator_may_watch_a_task_that_exists()
    {
        // Without this the refusal above could be a service that refuses everybody, and the whole
        // endpoint would be dead while its tests stayed green.
        var database = Guid.NewGuid().ToString();
        var task = TasksTestContext.Row();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(task);
            await seed.SaveChangesAsync();
        }

        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var service = new TaskStreamService(context, TasksTestContext.StreamOptions());

        var result = await service.ResolveAsync(task.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(task.Id, result.Value);
    }

    /// <summary>A task that does not exist is answered not found.</summary>
    [Fact]
    public async Task A_task_that_does_not_exist_is_answered_not_found()
    {
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin());
        var service = new TaskStreamService(context, TasksTestContext.StreamOptions());

        var result = await service.ResolveAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("TaskNotFound", result.Error!.Code);
    }
}
