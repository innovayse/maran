using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Queries.ListApps;
using Maran.Modules.Apps.Services;
using Maran.Modules.Apps.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Apps.Tests.Queries.ListApps;

/// <summary>Listing the apps: a row names a task only while its operation is in flight (issue #524).</summary>
public sealed class ListAppsQueryHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The doubles.</param>
    /// <returns>The handler.</returns>
    private static ListAppsQueryHandler HandlerOver(AppsWorld world)
    {
        return new ListAppsQueryHandler(
            world.Agent,
            world.Db,
            new AppDisplayNames(new KeyLocalizer()),
            new StaleOperationSettler(
                world.Db,
                world.TaskStates,
                world.Clock,
                NullLogger<StaleOperationSettler>.Instance));
    }

    /// <summary>A row an install settled answers no task, so the screen has nothing to follow.</summary>
    [Fact]
    public async Task A_settled_row_answers_no_task()
    {
        using var world = new AppsWorld();
        Assert.True(AppId.TryParse("php-8.3", out var installed));
        Assert.True(AppId.TryParse("php-8.4", out var failed));
        var installedRow = InstalledApp.BeginInstall(Guid.NewGuid(), installed, Guid.NewGuid(), AppsWorld.Now);
        installedRow.MarkInstalled("8.3", AppsWorld.Now);
        var failedRow = InstalledApp.BeginInstall(Guid.NewGuid(), failed, Guid.NewGuid(), AppsWorld.Now);
        failedRow.MarkFailed(AppsWorld.Now);
        world.Db.InstalledApps.AddRange(installedRow, failedRow);
        await world.Db.SaveChangesAsync();
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
            [new AgentApp("php-8.3", AgentAppKind.Runtime, "8.3", AgentAppServiceStatus.Active)]);

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(result.Value.Items, app => { Assert.Null(app.TaskId); });
    }

    /// <summary>The control: rows whose install or removal is running answer their tasks.</summary>
    [Fact]
    public async Task A_row_in_flight_answers_its_task()
    {
        using var world = new AppsWorld();
        Assert.True(AppId.TryParse("php-8.4", out var installing));
        Assert.True(AppId.TryParse("php-8.3", out var removing));
        var installTask = Guid.NewGuid();
        var removeTask = Guid.NewGuid();
        var installingRow = InstalledApp.BeginInstall(Guid.NewGuid(), installing, installTask, AppsWorld.Now);
        var removingRow = InstalledApp.BeginInstall(Guid.NewGuid(), removing, Guid.NewGuid(), AppsWorld.Now);
        removingRow.MarkInstalled("8.3", AppsWorld.Now);
        removingRow.BeginRemoval(removeTask, AppsWorld.Now);
        world.Db.InstalledApps.AddRange(installingRow, removingRow);
        await world.Db.SaveChangesAsync();
        world.TaskStates.States[installTask] = TaskState.Running;
        world.TaskStates.States[removeTask] = TaskState.Running;

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        var tasks = result.Value.Items.ToDictionary(app => { return app.AppId; }, app => { return app.TaskId; });
        Assert.Equal(installTask, tasks["php-8.4"]);
        Assert.Equal(removeTask, tasks["php-8.3"]);
    }

    /// <summary>Seeds a row left in flight by an operation, with its task in a given state.</summary>
    /// <param name="world">The doubles.</param>
    /// <param name="text">The catalogue id.</param>
    /// <param name="removing">Whether the row is a removal rather than an install.</param>
    /// <param name="state">The state the row's task answers; null scripts nothing, so the task is unknown.</param>
    /// <param name="taskId">The task id the row holds; a fresh one when omitted.</param>
    /// <returns>The row's task id.</returns>
    private static async Task<Guid> SeedBusyAsync(
        AppsWorld world,
        string text,
        bool removing,
        TaskState? state,
        Guid? taskId = null)
    {
        Assert.True(AppId.TryParse(text, out var appId));
        var task = taskId ?? Guid.NewGuid();
        var row = InstalledApp.BeginInstall(Guid.NewGuid(), appId, task, AppsWorld.Now);
        if (removing)
        {
            row.MarkInstalled("1", AppsWorld.Now);
            row.BeginRemoval(task, AppsWorld.Now);
        }

        world.Db.InstalledApps.Add(row);
        await world.Db.SaveChangesAsync();
        if (state is { } scripted)
        {
            world.TaskStates.States[task] = scripted;
        }

        return task;
    }

    /// <summary>An install whose task has ended and whose app the host lacks is settled as failed, with no task to follow, without a restart.</summary>
    [Theory]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.Unknown)]
    public async Task A_stale_install_the_host_lacks_is_settled_as_failed(TaskState ended)
    {
        using var world = new AppsWorld();
        await SeedBusyAsync(world, "php-8.4", removing: false, ended);

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        var app = Assert.Single(result.Value.Items);
        Assert.Equal(AppState.Failed, app.State);
        Assert.Null(app.TaskId);
        var stored = await world.Db.InstalledApps.AsNoTracking().SingleAsync();
        Assert.Equal(AppState.Failed, stored.State);
        Assert.Null(stored.TaskId);
    }

    /// <summary>An install whose task has ended and whose app the host has is settled as installed at the host's version.</summary>
    [Fact]
    public async Task A_stale_install_the_host_has_is_settled_as_installed()
    {
        using var world = new AppsWorld();
        await SeedBusyAsync(world, "php-8.3", removing: false, TaskState.Failed);
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
            [new AgentApp("php-8.3", AgentAppKind.Runtime, "8.3.9", AgentAppServiceStatus.Active)]);

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        var app = Assert.Single(result.Value.Items);
        Assert.Equal(AppState.Installed, app.State);
        Assert.Equal("8.3.9", app.Version);
        Assert.Null(app.TaskId);
        Assert.Equal(AppState.Installed, (await world.Db.InstalledApps.AsNoTracking().SingleAsync()).State);
    }

    /// <summary>A removal whose task is gone and whose app the host no longer has is finished: the row leaves the ledger.</summary>
    [Fact]
    public async Task A_stale_removal_the_host_no_longer_has_leaves_the_ledger()
    {
        using var world = new AppsWorld();
        await SeedBusyAsync(world, "php-8.1", removing: true, TaskState.Unknown);

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        Assert.Empty(result.Value.Items);
        Assert.Empty(await world.Db.InstalledApps.AsNoTracking().ToListAsync());
    }

    /// <summary>A removal whose task ended with the app still on the host is installed again.</summary>
    [Fact]
    public async Task A_stale_removal_the_host_still_has_is_installed_again()
    {
        using var world = new AppsWorld();
        await SeedBusyAsync(world, "php-8.1", removing: true, TaskState.Failed);
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
            [new AgentApp("php-8.1", AgentAppKind.Runtime, "8.1.1", AgentAppServiceStatus.Active)]);

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        Assert.Equal(AppState.Installed, Assert.Single(result.Value.Items).State);
        Assert.Equal(AppState.Installed, (await world.Db.InstalledApps.AsNoTracking().SingleAsync()).State);
    }

    /// <summary>The inverse control: a row whose task is still running is left alone, whatever the host says.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_row_whose_task_is_running_is_left_alone(bool removing)
    {
        using var world = new AppsWorld();
        var task = await SeedBusyAsync(world, "php-8.4", removing, TaskState.Running);

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        var app = Assert.Single(result.Value.Items);
        Assert.Equal(removing ? AppState.Removing : AppState.Installing, app.State);
        Assert.Equal(task, app.TaskId);
        var stored = await world.Db.InstalledApps.AsNoTracking().SingleAsync();
        Assert.Equal(removing ? AppState.Removing : AppState.Installing, stored.State);
        Assert.Equal(task, stored.TaskId);
    }

    /// <summary>The inverse control: a row holding the empty task id (nothing could be recorded) cannot be told from a live operation, and is not asked about.</summary>
    [Fact]
    public async Task A_row_with_the_empty_task_id_is_left_alone()
    {
        using var world = new AppsWorld();
        await SeedBusyAsync(world, "php-8.4", removing: false, state: null, taskId: Guid.Empty);

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        Assert.Equal(AppState.Installing, Assert.Single(result.Value.Items).State);
        Assert.Empty(world.TaskStates.Asked);
    }

    /// <summary>The inverse control: rows that are not busy are never judged, whatever their task would answer.</summary>
    [Fact]
    public async Task A_row_that_is_not_busy_is_not_asked_about()
    {
        using var world = new AppsWorld();
        Assert.True(AppId.TryParse("php-8.3", out var installed));
        var row = InstalledApp.BeginInstall(Guid.NewGuid(), installed, Guid.NewGuid(), AppsWorld.Now);
        row.MarkInstalled("8.3", AppsWorld.Now);
        world.Db.InstalledApps.Add(row);
        await world.Db.SaveChangesAsync();
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
            [new AgentApp("php-8.3", AgentAppKind.Runtime, "8.3", AgentAppServiceStatus.Active)]);

        await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        Assert.Empty(world.TaskStates.Asked);
        Assert.Equal(AppState.Installed, (await world.Db.InstalledApps.AsNoTracking().SingleAsync()).State);
    }

    /// <summary>A save that loses a race to another writer does not fail the read; the row stays as it was for the next read to judge.</summary>
    [Fact]
    public async Task A_lost_race_does_not_fail_the_read()
    {
        var rival = new ConflictOnceSaveInterceptor();
        using var world = new AppsWorld(rival);
        await using (var seed = world.RivalDb())
        {
            Assert.True(AppId.TryParse("php-8.4", out var appId));
            seed.InstalledApps.Add(InstalledApp.BeginInstall(Guid.NewGuid(), appId, Guid.NewGuid(), AppsWorld.Now));
            await seed.SaveChangesAsync();
        }

        var first = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);
        var second = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, rival.Refused);
        Assert.Equal(AppState.Failed, Assert.Single(second.Value.Items).State);
    }

    /// <summary>Seeds a busy row through a second context, so the handler's own context has not tracked it.</summary>
    /// <param name="world">The doubles.</param>
    /// <param name="text">The catalogue id.</param>
    /// <param name="taskId">The task the row holds.</param>
    /// <returns>The row's identity.</returns>
    private static async Task<Guid> SeedInstallingAsync(AppsWorld world, string text, Guid taskId)
    {
        Assert.True(AppId.TryParse(text, out var appId));
        await using var seed = world.RivalDb();
        var row = InstalledApp.BeginInstall(Guid.NewGuid(), appId, taskId, AppsWorld.Now);
        seed.InstalledApps.Add(row);
        await seed.SaveChangesAsync();
        return row.Id;
    }

    /// <summary>A row its own runner settled after its task was asked about is left as the runner left it, though the stale host list lacks the app.</summary>
    [Fact]
    public async Task A_row_settled_by_its_runner_meanwhile_is_not_deleted()
    {
        using var world = new AppsWorld();
        var movedTask = Guid.NewGuid();
        var quietTask = Guid.NewGuid();
        var movedId = await SeedInstallingAsync(world, "php-8.3", movedTask);
        await SeedInstallingAsync(world, "php-8.4", quietTask);
        world.TaskStates.States[movedTask] = TaskState.Completed;
        world.TaskStates.States[quietTask] = TaskState.Completed;
        world.TaskStates.WhileReading = async asked =>
        {
            if (asked != movedTask)
            {
                return;
            }

            await using var runner = world.RivalDb();
            var row = await runner.InstalledApps.SingleAsync(app => app.Id == movedId);
            row.MarkInstalled("8.3.9", AppsWorld.Now);
            await runner.SaveChangesAsync();
        };

        await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        var stored = await world.Db.InstalledApps.AsNoTracking().ToListAsync();
        var moved = Assert.Single(stored, app => { return app.AppId == "php-8.3"; });
        Assert.Equal(AppState.Installed, moved.State);
        Assert.Equal("8.3.9", moved.Version);

        // The inverse control: the row that did not move on is still judged in the same pass.
        Assert.Equal(AppState.Failed, Assert.Single(stored, app => { return app.AppId == "php-8.4"; }).State);
    }

    /// <summary>A row retried on a new task after its old task was asked about is a new operation: it is not failed while that operation runs.</summary>
    [Fact]
    public async Task A_row_retried_on_a_new_task_meanwhile_is_left_alone()
    {
        using var world = new AppsWorld();
        var oldTask = Guid.NewGuid();
        var newTask = Guid.NewGuid();
        var id = await SeedInstallingAsync(world, "php-8.4", oldTask);
        world.TaskStates.States[oldTask] = TaskState.Failed;
        world.TaskStates.States[newTask] = TaskState.Running;
        world.TaskStates.WhileReading = async _ =>
        {
            await using var retry = world.RivalDb();
            var row = await retry.InstalledApps.SingleAsync(app => app.Id == id);
            row.MarkFailed(AppsWorld.Now);
            row.RetryInstall(newTask, AppsWorld.Now);
            await retry.SaveChangesAsync();
        };

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        var app = Assert.Single(result.Value.Items);
        Assert.Equal(AppState.Installing, app.State);
        Assert.Equal(newTask, app.TaskId);
        var stored = await world.Db.InstalledApps.AsNoTracking().SingleAsync();
        Assert.Equal(AppState.Installing, stored.State);
        Assert.Equal(newTask, stored.TaskId);
    }

    /// <summary>A database fault while settling does not fail the read; the list is returned as it is, and the inverse control is that a healthy save settles.</summary>
    [Fact]
    public async Task A_database_fault_while_settling_does_not_fail_the_read()
    {
        using var world = new AppsWorld(new DbUpdateException("the disk is full"));
        var task = Guid.NewGuid();
        await SeedInstallingAsync(world, "php-8.4", task);
        world.TaskStates.States[task] = TaskState.Failed;

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        Assert.True(result.IsSuccess);
        var app = Assert.Single(result.Value.Items);
        Assert.Equal(AppState.Installing, app.State);
        Assert.Equal(task, app.TaskId);
    }
}
