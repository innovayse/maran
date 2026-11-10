using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Jobs;
using Maran.Modules.Apps.Services;
using Maran.Modules.Apps.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Apps.Tests.Services;

/// <summary>The job: the terminal event decides, the ledger settles before the task closes, and silence is never success.</summary>
public sealed class AppOperationRunnerTests
{
    /// <summary>The audience every test task is opened under.</summary>
    private const Maran.Sdk.Contracts.TaskAudience TaskAudienceOnly = Maran.Sdk.Contracts.TaskAudience.AdministratorOnly;

    /// <summary>Builds the runner over a world.</summary>
    /// <param name="world">The doubles.</param>
    /// <returns>The runner.</returns>
    private static AppOperationRunner RunnerOver(AppsWorld world)
    {
        return new AppOperationRunner(
            world.Agent,
            world.Tasks,
            world.Db,
            world.Journal,
            world.Clock,
            NullLogger<AppOperationRunner>.Instance);
    }

    /// <summary>Seeds a row in a state and opens its task.</summary>
    /// <param name="world">The doubles.</param>
    /// <param name="text">The catalogue id.</param>
    /// <param name="removing">Whether the row is being removed rather than installed.</param>
    /// <returns>The task id.</returns>
    private static async Task<Guid> SeedAsync(AppsWorld world, string text, bool removing)
    {
        Assert.True(AppId.TryParse(text, out var appId));
        var task = await world.Tasks.BeginAsync("k", text, null, TaskAudienceOnly, null, default);
        var row = InstalledApp.BeginInstall(Guid.NewGuid(), appId, task, AppsWorld.Now);
        if (removing)
        {
            row.MarkInstalled("1", AppsWorld.Now);
            row.BeginRemoval(task, AppsWorld.Now);
        }

        world.Db.InstalledApps.Add(row);
        await world.Db.SaveChangesAsync();
        return task;
    }

    /// <summary>A completed install settles the row at the host's version, reports progress and completes the task.</summary>
    [Fact]
    public async Task A_completed_install_settles_the_row_and_completes_the_task()
    {
        using var world = new AppsWorld();
        var task = await SeedAsync(world, "php-8.3", removing: false);
        world.Agent.Stream =
        [
            new(AppOperationEventKind.Progress, 40, "installing", null),
            new(AppOperationEventKind.Completed, 100, string.Empty, null),
        ];
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
            [new AgentApp("php-8.3", AgentAppKind.Runtime, "8.3", AgentAppServiceStatus.Active)]);

        await RunnerOver(world).RunAsync(new AppOperationRequested(task, AppOperation.Install, "php-8.3"), default);

        var row = await world.Db.InstalledApps.SingleAsync();
        Assert.Equal(AppState.Installed, row.State);
        Assert.Equal("8.3", row.Version);
        var recorded = Assert.Single(world.Tasks.Tasks);
        Assert.True(recorded.Completed);
        Assert.Contains(recorded.Reports, report => { return report.Percent == 40 && report.Line == "installing"; });
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal("AppInstalled", entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A failure from the agent fails the task under the agent's code and marks the row failed.</summary>
    [Fact]
    public async Task A_failed_install_fails_the_task_and_the_row()
    {
        using var world = new AppsWorld();
        var task = await SeedAsync(world, "nginx", removing: false);
        world.Agent.Stream =
        [
            new(AppOperationEventKind.Failed, 0, string.Empty, Error.Of("AgentPackageNotAvailable", ErrorType.Conflict)),
        ];

        await RunnerOver(world).RunAsync(new AppOperationRequested(task, AppOperation.Install, "nginx"), default);

        Assert.Equal(AppState.Failed, (await world.Db.InstalledApps.SingleAsync()).State);
        var recorded = Assert.Single(world.Tasks.Tasks);
        Assert.False(recorded.Completed);
        Assert.Equal("AgentPackageNotAvailable", recorded.FailureCode);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>A stream that ends without a terminal event is never reported as success.</summary>
    /// <param name="kind">How the stream ended.</param>
    [Theory]
    [InlineData(AppOperationEventKind.Truncated)]
    [InlineData(AppOperationEventKind.Dropped)]
    [InlineData(AppOperationEventKind.Idle)]
    [InlineData(AppOperationEventKind.Cancelled)]
    public async Task A_stream_without_an_outcome_is_not_a_success(AppOperationEventKind kind)
    {
        using var world = new AppsWorld();
        var task = await SeedAsync(world, "nginx", removing: false);
        world.Agent.Stream = [new(kind, 0, string.Empty, null)];

        await RunnerOver(world).RunAsync(new AppOperationRequested(task, AppOperation.Install, "nginx"), default);

        Assert.Equal(AppState.Failed, (await world.Db.InstalledApps.SingleAsync()).State);
        Assert.Equal("AppsOutcomeUnobserved", Assert.Single(world.Tasks.Tasks).FailureCode);
    }

    /// <summary>An empty stream is the same: nothing was observed.</summary>
    [Fact]
    public async Task An_empty_stream_is_not_a_success()
    {
        using var world = new AppsWorld();
        var task = await SeedAsync(world, "nginx", removing: false);
        world.Agent.Stream = [];

        await RunnerOver(world).RunAsync(new AppOperationRequested(task, AppOperation.Install, "nginx"), default);

        Assert.Equal("AppsOutcomeUnobserved", Assert.Single(world.Tasks.Tasks).FailureCode);
    }

    /// <summary>A completed removal deletes the row.</summary>
    [Fact]
    public async Task A_completed_removal_deletes_the_row()
    {
        using var world = new AppsWorld();
        var task = await SeedAsync(world, "php-8.3", removing: true);

        await RunnerOver(world).RunAsync(new AppOperationRequested(task, AppOperation.Uninstall, "php-8.3"), default);

        Assert.Empty(world.Db.InstalledApps);
        Assert.True(Assert.Single(world.Tasks.Tasks).Completed);
        Assert.Equal("AppUninstalled", Assert.Single(world.Audit.Entries).Action);
    }

    /// <summary>A refused removal asks the host what it has: still there means installed again.</summary>
    [Fact]
    public async Task A_refused_removal_returns_the_row_to_installed_when_the_host_still_has_the_app()
    {
        using var world = new AppsWorld();
        var task = await SeedAsync(world, "php-8.3", removing: true);
        world.Agent.Stream = [new(AppOperationEventKind.Failed, 0, string.Empty, Error.Of("AgentAppInUse", ErrorType.Conflict))];
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
            [new AgentApp("php-8.3", AgentAppKind.Runtime, "8.3", AgentAppServiceStatus.Active)]);

        await RunnerOver(world).RunAsync(new AppOperationRequested(task, AppOperation.Uninstall, "php-8.3"), default);

        Assert.Equal(AppState.Installed, (await world.Db.InstalledApps.SingleAsync()).State);
        Assert.Equal("AgentAppInUse", Assert.Single(world.Tasks.Tasks).FailureCode);
    }

    /// <summary>The control: when the host cannot be asked, the row says failed rather than guessing.</summary>
    [Fact]
    public async Task A_refused_removal_with_an_unreachable_host_marks_the_row_failed()
    {
        using var world = new AppsWorld();
        var task = await SeedAsync(world, "php-8.3", removing: true);
        world.Agent.Stream = [new(AppOperationEventKind.Failed, 0, string.Empty, Error.Of("AgentUnavailable", ErrorType.Unavailable))];
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable));

        await RunnerOver(world).RunAsync(new AppOperationRequested(task, AppOperation.Uninstall, "php-8.3"), default);

        Assert.Equal(AppState.Failed, (await world.Db.InstalledApps.SingleAsync()).State);
    }
}
