using Maran.Modules.Tasks.Services;
using Maran.Modules.Tasks.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Tasks.Tests.Services;

/// <summary>
/// What the Sdk window onto tasks answers (issue #533): the state of a task by id, for a pass that has no
/// signed-in account of the task's.
/// </summary>
public sealed class TaskStateReaderTests
{
    /// <summary>A running, a completed and a failed task answer their own state.</summary>
    [Fact]
    public async Task A_task_answers_its_state()
    {
        var database = Guid.NewGuid().ToString();
        var running = TasksTestContext.Row();
        var completed = TasksTestContext.Row();
        completed.Complete(TasksTestContext.Now);
        var failed = TasksTestContext.Row();
        failed.Fail("SomethingBroke", TasksTestContext.Now);
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.AddRange(running, completed, failed);
            await seed.SaveChangesAsync();
        }

        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin(), database);
        var reader = new TaskStateReader(context);

        Assert.Equal(TaskState.Running, await reader.ReadAsync(running.Id, default));
        Assert.Equal(TaskState.Completed, await reader.ReadAsync(completed.Id, default));
        Assert.Equal(TaskState.Failed, await reader.ReadAsync(failed.Id, default));
    }

    /// <summary>No task has the id, or the id is the empty one the recorder answers when it recorded nothing.</summary>
    [Fact]
    public async Task An_id_no_task_has_is_unknown()
    {
        await using var context = TasksTestContext.Create(FakeCurrentUser.Admin());
        var reader = new TaskStateReader(context);

        Assert.Equal(TaskState.Unknown, await reader.ReadAsync(Guid.NewGuid(), default));
        Assert.Equal(TaskState.Unknown, await reader.ReadAsync(Guid.Empty, default));
    }

    /// <summary>
    /// A running task is reported running whoever the context is bound to: the module's visibility filter
    /// would hide an administrator-only task from a customer and read as Unknown, which is the answer that
    /// lets a caller settle work in flight.
    /// </summary>
    [Fact]
    public async Task A_running_task_is_running_whatever_the_filter_would_show()
    {
        var database = Guid.NewGuid().ToString();
        var hidden = TasksTestContext.Row();
        await using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.PanelTasks.Add(hidden);
            await seed.SaveChangesAsync();
        }

        await using var customer = TasksTestContext.Create(FakeCurrentUser.Customer(), database);
        Assert.Empty(customer.PanelTasks.ToList());

        Assert.Equal(TaskState.Running, await new TaskStateReader(customer).ReadAsync(hidden.Id, default));
    }
}
