using Maran.Modules.Apps.Commands.InstallApp;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Apps.Tests.Commands.InstallApp;

/// <summary>Installing an app: accepted as a recorded task, or refused with a coded reason that changes nothing.</summary>
public sealed class InstallAppCommandHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The doubles.</param>
    /// <returns>The handler.</returns>
    private static InstallAppCommandHandler HandlerOver(AppsWorld world)
    {
        return new InstallAppCommandHandler(
            world.Agent,
            world.Tasks,
            new StubCorrelationIdAccessor("corr-1"),
            world.Db,
            world.Dispatcher,
            world.Journal,
            world.Clock);
    }

    /// <summary>Adds a ledger row in a state.</summary>
    /// <param name="world">The doubles.</param>
    /// <param name="text">The catalogue id.</param>
    /// <param name="state">The state.</param>
    /// <returns>The task the row carries.</returns>
    private static Guid Seed(AppsWorld world, string text, AppState state)
    {
        Assert.True(AppId.TryParse(text, out var appId));
        var task = Guid.NewGuid();
        var row = InstalledApp.BeginInstall(Guid.NewGuid(), appId, task, AppsWorld.Now);
        if (state == AppState.Installed)
        {
            row.MarkInstalled("1", AppsWorld.Now);
        }
        else if (state == AppState.Failed)
        {
            row.MarkFailed(AppsWorld.Now);
        }
        else if (state == AppState.Removing)
        {
            row.MarkInstalled("1", AppsWorld.Now);
            row.BeginRemoval(task, AppsWorld.Now);
        }

        world.Db.InstalledApps.Add(row);
        world.Db.SaveChanges();
        return task;
    }

    /// <summary>An offered app is accepted: a task, an installing row, a hand-over and an audit entry.</summary>
    [Fact]
    public async Task An_offered_app_is_accepted_as_a_recorded_task()
    {
        using var world = new AppsWorld();
        world.Offer("nginx");

        var result = await HandlerOver(world).HandleAsync(new InstallAppCommand("nginx", "203.0.113.5", "ua"), default);

        Assert.True(result.IsSuccess);
        var task = Assert.Single(world.Tasks.Tasks);
        Assert.Equal(TaskKinds.AppInstall, task.Kind);
        Assert.Equal("nginx", task.Subject);
        Assert.Equal(TaskAudience.AdministratorOnly, task.Audience);
        Assert.Equal(task.Id, result.Value.TaskId);

        var row = await world.Db.InstalledApps.SingleAsync();
        Assert.Equal(AppState.Installing, row.State);
        Assert.Equal(task.Id, row.TaskId);

        var message = Assert.Single(world.Dispatcher.Dispatched);
        Assert.Equal(AppOperation.Install, message.Operation);
        Assert.Equal("nginx", message.AppId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal("AppInstallRequested", entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An app the host's sources do not offer is refused before a task exists.</summary>
    [Fact]
    public async Task An_app_the_sources_do_not_offer_is_refused_without_a_task()
    {
        using var world = new AppsWorld();
        world.Offer("nginx");

        var result = await HandlerOver(world).HandleAsync(new InstallAppCommand("php-8.4"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("AppsNotInstallable", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(world.Tasks.Tasks);
        Assert.Empty(world.Dispatcher.Dispatched);
        Assert.Empty(world.Db.InstalledApps);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>Only one site web server at a time, and the panel refuses before asking the agent anything.</summary>
    /// <param name="rivalState">The rival's state.</param>
    [Theory]
    [InlineData(AppState.Installed)]
    [InlineData(AppState.Installing)]
    [InlineData(AppState.Removing)]
    public async Task A_second_web_server_is_refused(AppState rivalState)
    {
        using var world = new AppsWorld();
        world.Offer("apache");
        Seed(world, "nginx", rivalState);

        var result = await HandlerOver(world).HandleAsync(new InstallAppCommand("apache"), default);

        Assert.Equal("AppsWebServerAlreadyInstalled", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(world.Tasks.Tasks);
        Assert.Empty(world.Dispatcher.Dispatched);
    }

    /// <summary>The control for the rule above: a failed rival does not hold the ports, and a runtime never conflicts with a web server.</summary>
    [Fact]
    public async Task A_failed_rival_and_a_runtime_do_not_block()
    {
        using var world = new AppsWorld();
        world.Offer("apache", "php-8.3");
        Seed(world, "nginx", AppState.Failed);

        Assert.True((await HandlerOver(world).HandleAsync(new InstallAppCommand("apache"), default)).IsSuccess);
        Assert.True((await HandlerOver(world).HandleAsync(new InstallAppCommand("php-8.3"), default)).IsSuccess);
    }

    /// <summary>An app that is already installed is refused and nothing is started.</summary>
    [Fact]
    public async Task An_installed_app_is_refused()
    {
        using var world = new AppsWorld();
        world.Offer("php-8.3");
        Seed(world, "php-8.3", AppState.Installed);

        var result = await HandlerOver(world).HandleAsync(new InstallAppCommand("php-8.3"), default);

        Assert.Equal("AppsAlreadyInstalled", result.Error!.Code);
        Assert.Empty(world.Tasks.Tasks);
    }

    /// <summary>A repeat of a running install is answered with the running task and creates nothing.</summary>
    [Fact]
    public async Task A_repeat_of_a_running_install_returns_the_running_task()
    {
        using var world = new AppsWorld();
        world.Offer("php-8.3");
        var running = Seed(world, "php-8.3", AppState.Installing);

        var result = await HandlerOver(world).HandleAsync(new InstallAppCommand("php-8.3"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(running, result.Value.TaskId);
        Assert.Empty(world.Tasks.Tasks);
        Assert.Empty(world.Dispatcher.Dispatched);
    }

    /// <summary>An app being removed cannot be installed over.</summary>
    [Fact]
    public async Task An_app_being_removed_is_busy()
    {
        using var world = new AppsWorld();
        world.Offer("php-8.3");
        Seed(world, "php-8.3", AppState.Removing);

        var result = await HandlerOver(world).HandleAsync(new InstallAppCommand("php-8.3"), default);

        Assert.Equal("AppsOperationInProgress", result.Error!.Code);
    }

    /// <summary>A failed install is retried on its own row with a new task.</summary>
    [Fact]
    public async Task A_failed_install_is_retried_on_the_same_row()
    {
        using var world = new AppsWorld();
        world.Offer("php-8.3");
        var first = Seed(world, "php-8.3", AppState.Failed);

        var result = await HandlerOver(world).HandleAsync(new InstallAppCommand("php-8.3"), default);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(first, result.Value.TaskId);
        var row = await world.Db.InstalledApps.SingleAsync();
        Assert.Equal(AppState.Installing, row.State);
    }

    /// <summary>A bus that is down leaves no open task and no row claiming to be installing, and the fault is not swallowed.</summary>
    [Fact]
    public async Task A_dispatch_that_throws_closes_the_task_and_fails_the_row()
    {
        using var world = new AppsWorld();
        world.Offer("nginx");
        world.Dispatcher.Throws = true;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await HandlerOver(world).HandleAsync(new InstallAppCommand("nginx"), default);
        });

        Assert.Equal("AppsOperationFailed", Assert.Single(world.Tasks.Tasks).FailureCode);
        Assert.Equal(AppState.Failed, (await world.Db.InstalledApps.SingleAsync()).State);
    }
}
