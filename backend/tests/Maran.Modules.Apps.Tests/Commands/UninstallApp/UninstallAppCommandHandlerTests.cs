using Maran.Modules.Apps.Commands.UninstallApp;
using Maran.Modules.Apps.Common;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Services;
using Maran.Modules.Apps.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Apps.Tests.Commands.UninstallApp;

/// <summary>Removing an app: refused while a site uses it, with the sites listed; otherwise a recorded task.</summary>
public sealed class UninstallAppCommandHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The doubles.</param>
    /// <returns>The handler.</returns>
    private static UninstallAppCommandHandler HandlerOver(AppsWorld world)
    {
        return new UninstallAppCommandHandler(
            world.Tasks,
            new StubCorrelationIdAccessor("corr-1"),
            world.Db,
            new AppUsageGuard(world.Sites),
            world.Dispatcher,
            world.Journal,
            world.Clock);
    }

    /// <summary>Adds an installed row.</summary>
    /// <param name="world">The doubles.</param>
    /// <param name="text">The catalogue id.</param>
    private static void Install(AppsWorld world, string text)
    {
        Assert.True(AppId.TryParse(text, out var appId));
        world.Db.InstalledApps.Add(InstalledApp.Discover(Guid.NewGuid(), appId, "1", AppsWorld.Now));
        world.Db.SaveChanges();
    }

    /// <summary>An unused app is removed as a recorded task.</summary>
    [Fact]
    public async Task An_unused_app_is_accepted_as_a_recorded_task()
    {
        using var world = new AppsWorld();
        Install(world, "php-8.3");

        var result = await HandlerOver(world).HandleAsync(new UninstallAppCommand("php-8.3", "203.0.113.5", "ua"), default);

        Assert.True(result.IsSuccess);
        var task = Assert.Single(world.Tasks.Tasks);
        Assert.Equal(TaskKinds.AppUninstall, task.Kind);
        Assert.Equal(AppState.Removing, (await world.Db.InstalledApps.SingleAsync()).State);
        Assert.Equal(AppOperation.Uninstall, Assert.Single(world.Dispatcher.Dispatched).Operation);
        Assert.Equal("AppUninstallRequested", Assert.Single(world.Audit.Entries).Action);
    }

    /// <summary>
    /// A PHP version a site is bound to cannot be removed: 409 <c>AppsInUse</c> with the domains in the
    /// problem's <c>sites</c> extension, and nothing started.
    /// </summary>
    [Fact]
    public async Task A_php_version_a_site_uses_is_refused_with_the_sites_listed()
    {
        using var world = new AppsWorld();
        Install(world, "php-8.3");
        world.Sites.OnPhp["8.3"] = ["a.example", "b.example"];

        var result = await HandlerOver(world).HandleAsync(new UninstallAppCommand("php-8.3"), default);

        Assert.Equal("AppsInUse", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("sites", result.Extension!.Key);
        var listed = Assert.IsType<AppInUseDto>(result.Extension.Value);
        Assert.Equal(2, listed.Total);
        Assert.Equal(["a.example", "b.example"], listed.Domains);
        Assert.Empty(world.Tasks.Tasks);
        Assert.Empty(world.Dispatcher.Dispatched);
        Assert.Equal(AppState.Installed, (await world.Db.InstalledApps.SingleAsync()).State);
    }

    /// <summary>The control: sites on another version do not block this one.</summary>
    [Fact]
    public async Task Sites_on_another_version_do_not_block()
    {
        using var world = new AppsWorld();
        Install(world, "php-8.3");
        world.Sites.OnPhp["8.2"] = ["a.example"];

        Assert.True((await HandlerOver(world).HandleAsync(new UninstallAppCommand("php-8.3"), default)).IsSuccess);
    }

    /// <summary>Any site blocks the web server, and a Node.js version is blocked by its own sites.</summary>
    [Fact]
    public async Task Any_site_blocks_the_web_server_and_node_sites_block_node()
    {
        using var world = new AppsWorld();
        Install(world, "nginx");
        Install(world, "nodejs-22");
        world.Sites.OnWebServer.Add("a.example");
        world.Sites.OnNode["22"] = ["n.example"];

        Assert.Equal("AppsInUse", (await HandlerOver(world).HandleAsync(new UninstallAppCommand("nginx"), default)).Error!.Code);
        Assert.Equal("AppsInUse", (await HandlerOver(world).HandleAsync(new UninstallAppCommand("nodejs-22"), default)).Error!.Code);
    }

    /// <summary>The list is capped and the true total is stated.</summary>
    [Fact]
    public async Task A_long_list_is_capped_with_the_true_total()
    {
        using var world = new AppsWorld();
        Install(world, "nginx");
        world.Sites.OnWebServer.AddRange(Enumerable.Range(0, 120).Select(i => { return $"s{i:000}.example"; }));

        var result = await HandlerOver(world).HandleAsync(new UninstallAppCommand("nginx"), default);

        var listed = Assert.IsType<AppInUseDto>(result.Extension!.Value);
        Assert.Equal(120, listed.Total);
        Assert.Equal(AppUsageGuard.ListedDomains, listed.Domains.Count);
    }

    /// <summary>An app the ledger does not hold is not installed.</summary>
    [Fact]
    public async Task An_unknown_app_is_not_installed()
    {
        using var world = new AppsWorld();

        var result = await HandlerOver(world).HandleAsync(new UninstallAppCommand("php-8.3"), default);

        Assert.Equal("AppsNotInstalled", result.Error!.Code);
        Assert.Empty(world.Tasks.Tasks);
    }

    /// <summary>
    /// Issue #526: two removals that read the same installed row cannot both start. The rival begins its removal
    /// between this request's read and its write, so this one gets the typed conflict and no uninstall is dispatched.
    /// </summary>
    [Fact]
    public async Task A_removal_that_loses_the_race_is_a_conflict()
    {
        using var world = new AppsWorld();
        Install(world, "php-8.3");
        var rivalTask = Guid.NewGuid();
        world.Tasks.AfterBegin = () =>
        {
            using var rival = world.RivalDb();
            rival.InstalledApps.Single().BeginRemoval(rivalTask, AppsWorld.Now);
            rival.SaveChanges();
        };

        var result = await HandlerOver(world).HandleAsync(new UninstallAppCommand("php-8.3"), default);

        Assert.Equal("AppsOperationInProgress", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("AppsOperationInProgress", Assert.Single(world.Tasks.Tasks).FailureCode);
        Assert.Empty(world.Dispatcher.Dispatched);
        using var check = world.RivalDb();
        Assert.Equal(rivalTask, check.InstalledApps.Single().TaskId);
    }

    /// <summary>The control: with no rival the same removal is accepted and dispatched once.</summary>
    [Fact]
    public async Task A_removal_with_no_rival_is_dispatched_once()
    {
        using var world = new AppsWorld();
        Install(world, "php-8.3");

        var result = await HandlerOver(world).HandleAsync(new UninstallAppCommand("php-8.3"), default);

        Assert.True(result.IsSuccess);
        Assert.Single(world.Dispatcher.Dispatched);
    }
}
