using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Services;
using Maran.Modules.Tasks.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Tasks.Tests.Services;

/// <summary>
/// What happens to the panel's tasks when the process comes back up. A task is opened by one process
/// and closed by the same one, so this pass is the only thing that ever closes a task the previous
/// process left running — and the only thing standing between an operator and a spinner that turns
/// for ever.
/// </summary>
public sealed class StartupTaskReconcilerTests
{
    /// <summary>The instant this process is treated as having started.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A task the previous process left running is closed as failed by the restart.</summary>
    [Fact]
    public async Task A_task_the_previous_process_left_running_is_closed_as_failed_by_the_restart()
    {
        var older = TasksTestContext.Row(startedAt: Now.AddMinutes(-30));
        using var world = new ReconcilerWorld([older]);

        Assert.Equal(1, await world.Reconciler.ReconcileAsync(CancellationToken.None));

        var stored = await world.ReadAsync(older.Id);
        Assert.Equal(PanelTaskStatus.Failed, stored.Status);
        Assert.Equal("TaskAbandonedByRestart", stored.ErrorCode);
        Assert.Equal(Now, stored.FinishedAt);
    }

    /// <summary>A task started by this process is never closed by the pass.</summary>
    /// <remarks>
    /// A hosted service starts alongside the web server, not strictly before it, so the first
    /// request's task can already be in the table while this pass is reading it. Closing every
    /// running row would kill a live operation's record on every boot — the same failure the class
    /// exists to fix, caused by the fix.
    /// </remarks>
    [Fact]
    public async Task A_task_started_by_this_process_is_never_closed_by_the_pass()
    {
        var live = TasksTestContext.Row(startedAt: Now.AddSeconds(5));
        using var world = new ReconcilerWorld([live]);

        Assert.Equal(0, await world.Reconciler.ReconcileAsync(CancellationToken.None));

        var stored = await world.ReadAsync(live.Id);
        Assert.Equal(PanelTaskStatus.Running, stored.Status);
        Assert.Null(stored.ErrorCode);
    }

    /// <summary>A task that had already finished is left exactly as it was.</summary>
    [Fact]
    public async Task A_task_that_had_already_finished_is_left_exactly_as_it_was()
    {
        // The first outcome stands. A completed deletion rewritten as "the panel restarted" would
        // tell an operator that destruction did not happen when it did.
        var completed = TasksTestContext.Row(startedAt: Now.AddHours(-2));
        completed.Complete(Now.AddHours(-1));
        var failed = TasksTestContext.Row(startedAt: Now.AddHours(-2));
        failed.Fail("AcmeAuthorityUnreachable", Now.AddHours(-1));
        using var world = new ReconcilerWorld([completed, failed]);

        Assert.Equal(0, await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal(PanelTaskStatus.Completed, (await world.ReadAsync(completed.Id)).Status);
        Assert.Equal("AcmeAuthorityUnreachable", (await world.ReadAsync(failed.Id)).ErrorCode);
    }

    /// <summary>Every abandoned task is closed and not merely the first one found.</summary>
    [Fact]
    public async Task Every_abandoned_task_is_closed_and_not_merely_the_first_one_found()
    {
        var first = TasksTestContext.Row(subject: "one.example", startedAt: Now.AddMinutes(-30));
        var second = TasksTestContext.Row(subject: "two.example", startedAt: Now.AddMinutes(-20));
        var third = TasksTestContext.Row(subject: "three.example", startedAt: Now.AddMinutes(-10));
        using var world = new ReconcilerWorld([first, second, third]);

        Assert.Equal(3, await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.All(await world.ReadAllAsync(), task =>
        {
            Assert.Equal(PanelTaskStatus.Failed, task.Status);
        });
    }

    /// <summary>The pass closes abandoned tasks although it runs as nobody at all.</summary>
    /// <remarks>
    /// The module's query filter admits administrators, and a hosted service has no signed-in caller
    /// — so a filtered read would find nothing, every time, and the reconciler would appear to work
    /// while doing nothing at all. This drives it through a context bound to a CUSTOMER, which is
    /// the least privileged principal the filter admits least, and still expects the row closed.
    /// </remarks>
    [Fact]
    public async Task The_pass_closes_abandoned_tasks_although_it_runs_as_nobody_at_all()
    {
        var older = TasksTestContext.Row(startedAt: Now.AddMinutes(-30));
        using var world = new ReconcilerWorld([older], FakeCurrentUser.Customer());

        Assert.Equal(1, await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal(PanelTaskStatus.Failed, (await world.ReadAsync(older.Id)).Status);
    }

    /// <summary>A restart with nothing left running closes nothing and says so.</summary>
    [Fact]
    public async Task A_restart_with_nothing_left_running_closes_nothing_and_says_so()
    {
        using var world = new ReconcilerWorld([]);

        Assert.Equal(0, await world.Reconciler.ReconcileAsync(CancellationToken.None));
    }

    /// <summary>The store and the reconciler under test.</summary>
    private sealed class ReconcilerWorld : IDisposable
    {
        /// <summary>The in-memory database the rows live in.</summary>
        private readonly string _database = Guid.NewGuid().ToString();

        /// <summary>The context every scope of the reconciler resolves.</summary>
        private readonly TasksDbContext _dbContext;

        /// <summary>The container the reconciler's scopes come from.</summary>
        private readonly TestScopeFactory _scopes;

        /// <summary>The reconciler under test.</summary>
        public StartupTaskReconciler Reconciler { get; }

        /// <summary>Seeds the store and builds the reconciler over it.</summary>
        /// <param name="tasks">The rows the previous process left behind.</param>
        /// <param name="currentUser">The principal the pass's context is bound to; nobody privileged by default.</param>
        public ReconcilerWorld(IReadOnlyList<PanelTask> tasks, FakeCurrentUser? currentUser = null)
        {
            using (var seed = TasksTestContext.Create(FakeCurrentUser.Admin(), _database))
            {
                seed.PanelTasks.AddRange(tasks);
                seed.SaveChanges();
            }

            _dbContext = TasksTestContext.Create(currentUser ?? FakeCurrentUser.Customer(), _database);
            _scopes = new TestScopeFactory(_dbContext);
            Reconciler = new StartupTaskReconciler(
                _scopes.Scopes, new FakeClock(Now), NullLogger<StartupTaskReconciler>.Instance);
        }

        /// <summary>Reads one row back through a fresh administrator context.</summary>
        /// <param name="id">The task to read.</param>
        /// <returns>The stored row.</returns>
        public async Task<PanelTask> ReadAsync(Guid id)
        {
            await using var reader = TasksTestContext.Create(FakeCurrentUser.Admin(), _database);
            return await reader.PanelTasks.AsNoTracking().SingleAsync(task => task.Id == id);
        }

        /// <summary>Reads every row back through a fresh administrator context.</summary>
        /// <returns>The stored rows.</returns>
        public async Task<List<PanelTask>> ReadAllAsync()
        {
            await using var reader = TasksTestContext.Create(FakeCurrentUser.Admin(), _database);
            return await reader.PanelTasks.AsNoTracking().ToListAsync();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Reconciler.Dispose();
            _scopes.Dispose();
            _dbContext.Dispose();
        }
    }
}
