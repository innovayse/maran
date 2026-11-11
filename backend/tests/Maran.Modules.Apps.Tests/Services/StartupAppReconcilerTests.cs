using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Persistence.Migrations;
using Maran.Modules.Apps.Services;
using Maran.Modules.Apps.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Apps.Tests.Services;

/// <summary>The start-up pass: the host wins over the ledger, in every direction.</summary>
public sealed class StartupAppReconcilerTests
{
    /// <summary>Builds the pass over a world.</summary>
    /// <param name="world">The doubles.</param>
    /// <returns>The reconciler and the provider that owns its scope.</returns>
    private static (StartupAppReconciler Reconciler, ServiceProvider Provider) Over(AppsWorld world)
    {
        var services = new ServiceCollection();
        services.AddSingleton(world.Db);
        services.AddSingleton<IAgentAppsClient>(world.Agent);
        var provider = services.BuildServiceProvider();

        return (
            new StartupAppReconciler(
                provider.GetRequiredService<IServiceScopeFactory>(),
                world.Clock,
                NullLogger<StartupAppReconciler>.Instance),
            provider);
    }

    /// <summary>Adds a row from its steps.</summary>
    /// <param name="world">The doubles.</param>
    /// <param name="text">The catalogue id.</param>
    /// <param name="state">The state to leave it in.</param>
    private static void Seed(AppsWorld world, string text, AppState state)
    {
        Assert.True(AppId.TryParse(text, out var appId));
        var row = InstalledApp.BeginInstall(Guid.NewGuid(), appId, Guid.NewGuid(), AppsWorld.Now);
        if (state == AppState.Installed)
        {
            row.MarkInstalled("1", AppsWorld.Now);
        }

        world.Db.InstalledApps.Add(row);
        world.Db.SaveChanges();
    }

    /// <summary>An app the host has and the ledger lacks is recorded as installed; one stuck installing that the host lacks fails; one gone from the host is removed.</summary>
    [Fact]
    public async Task The_ledger_is_settled_against_the_host()
    {
        using var world = new AppsWorld();
        Seed(world, "php-8.2", AppState.Installing);
        Seed(world, "php-8.1", AppState.Installed);
        Seed(world, "nodejs-22", AppState.Installing);
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
        [
            new AgentApp("nginx", AgentAppKind.WebServer, "1.24.0", AgentAppServiceStatus.Active),
            new AgentApp("nodejs-22", AgentAppKind.Runtime, "22.1", AgentAppServiceStatus.None),
            new AgentApp("unknown-thing", AgentAppKind.Runtime, "1", AgentAppServiceStatus.None),
        ]);
        var (reconciler, provider) = Over(world);
        using var disposable = provider;

        var settled = await reconciler.ReconcileAsync(default);

        Assert.True(settled);
        var rows = await world.Db.InstalledApps.ToDictionaryAsync(row =>
        {
            return row.AppId;
        });
        Assert.Equal(AppState.Installed, rows["nginx"].State);
        Assert.Equal("1.24.0", rows["nginx"].Version);
        Assert.Equal(AppState.Installed, rows["nodejs-22"].State);
        Assert.Equal(AppState.Failed, rows["php-8.2"].State);
        Assert.False(rows.ContainsKey("php-8.1"));
        Assert.False(rows.ContainsKey("unknown-thing"));
    }

    /// <summary>An agent that is not answering leaves the ledger as it is and says the pass did not settle.</summary>
    [Fact]
    public async Task An_unreachable_agent_changes_nothing()
    {
        using var world = new AppsWorld();
        Seed(world, "php-8.1", AppState.Installed);
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable));
        var (reconciler, provider) = Over(world);
        using var disposable = provider;

        var settled = await reconciler.ReconcileAsync(default);

        Assert.False(settled);
        Assert.Equal(AppState.Installed, (await world.Db.InstalledApps.SingleAsync()).State);
    }

    /// <summary>The web server the marker names changed by hand: the old row is released before the new one is recorded, so the one-web-server index never sees both.</summary>
    [Fact]
    public async Task A_changed_web_server_is_released_before_the_new_one_is_recorded()
    {
        using var world = new AppsWorld(new ActiveWebServerIndexInterceptor());
        Seed(world, "nginx", AppState.Installed);
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
        [
            new AgentApp("apache", AgentAppKind.WebServer, "2.4", AgentAppServiceStatus.Active),
        ]);
        var (reconciler, provider) = Over(world);
        using var disposable = provider;

        var settled = await reconciler.ReconcileAsync(default);

        Assert.True(settled);
        var row = await world.Db.InstalledApps.SingleAsync();
        Assert.Equal("apache", row.AppId);
        Assert.Equal(AppState.Installed, row.State);
    }

    /// <summary>A failed web server row the host has again takes the place of the installed one the host lost, in the same pass.</summary>
    [Fact]
    public async Task A_failed_web_server_the_host_has_replaces_the_installed_one_it_lost()
    {
        using var world = new AppsWorld(new ActiveWebServerIndexInterceptor());
        Seed(world, "nginx", AppState.Installed);
        Seed(world, "apache", AppState.Installing);
        var apache = await world.Db.InstalledApps.SingleAsync(row => row.AppId == "apache");
        apache.MarkFailed(AppsWorld.Now);
        world.Db.SaveChanges();
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
        [
            new AgentApp("apache", AgentAppKind.WebServer, "2.4", AgentAppServiceStatus.Active),
        ]);
        var (reconciler, provider) = Over(world);
        using var disposable = provider;

        Assert.True(await reconciler.ReconcileAsync(default));

        var rows = await world.Db.InstalledApps.ToDictionaryAsync(row => { return row.AppId; });
        Assert.Equal(AppState.Installed, rows["apache"].State);
        Assert.False(rows.ContainsKey("nginx"));
    }

    /// <summary>The ids that remain once the second web server is left out.</summary>
    private static readonly string[] ExpectedIds = ["nginx", "php-8.3"];

    /// <summary>A host that reports two web servers is recorded with the one the ledger already holds; the pass still settles.</summary>
    [Fact]
    public async Task A_second_web_server_on_the_host_is_left_out_and_the_pass_settles()
    {
        using var world = new AppsWorld(new ActiveWebServerIndexInterceptor());
        Seed(world, "nginx", AppState.Installed);
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
        [
            new AgentApp("nginx", AgentAppKind.WebServer, "1", AgentAppServiceStatus.Active),
            new AgentApp("apache", AgentAppKind.WebServer, "2.4", AgentAppServiceStatus.Active),
            new AgentApp("php-8.3", AgentAppKind.Runtime, "8.3", AgentAppServiceStatus.None),
        ]);
        var (reconciler, provider) = Over(world);
        using var disposable = provider;

        Assert.True(await reconciler.ReconcileAsync(default));

        var ids = (await world.Db.InstalledApps.ToListAsync()).Select(row => { return row.AppId; }).Order().ToList();
        Assert.Equal(ExpectedIds, ids.ToArray());
    }

    /// <summary>The control: the stand-in for the index refuses a save that holds the old web server and the new one together, so the tests above would fail on a single save.</summary>
    [Fact]
    public async Task The_control_index_stand_in_refuses_one_save_holding_both_web_servers()
    {
        using var world = new AppsWorld(new ActiveWebServerIndexInterceptor());
        Seed(world, "nginx", AppState.Installed);
        Assert.True(AppId.TryParse("apache", out var apache));
        world.Db.InstalledApps.Remove(await world.Db.InstalledApps.SingleAsync());
        world.Db.InstalledApps.Add(InstalledApp.Discover(Guid.NewGuid(), apache, "2.4", AppsWorld.Now));

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => { return world.Db.SaveChangesAsync(); });

        Assert.NotNull(AppLedgerConflicts.Classify(refused));
    }

    /// <summary>A save that lost a race is judged again from a fresh read, and the pass settles.</summary>
    [Fact]
    public async Task A_save_that_lost_a_race_is_read_again_and_the_pass_settles()
    {
        var rival = new ConflictOnceSaveInterceptor();
        using var world = new AppsWorld(rival);
        Seed(world, "php-8.2", AppState.Installing);
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
        [
            new AgentApp("nginx", AgentAppKind.WebServer, "1.24.0", AgentAppServiceStatus.Active),
        ]);
        var (reconciler, provider) = Over(world);
        using var disposable = provider;

        var settled = await reconciler.ReconcileAsync(default);

        Assert.True(settled);
        Assert.Equal(1, rival.Refused);
        var rows = await world.Db.InstalledApps.ToDictionaryAsync(row => { return row.AppId; });
        Assert.Equal(AppState.Failed, rows["php-8.2"].State);
        Assert.Equal(AppState.Installed, rows["nginx"].State);
    }

    /// <summary>The control: a save that fails for any other reason is not retried as a conflict; it escapes to the caller.</summary>
    [Fact]
    public async Task A_fault_that_is_not_a_conflict_escapes()
    {
        using var world = new AppsWorld(new DbUpdateException("disk full"));
        Seed(world, "php-8.2", AppState.Installing);
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok([]);
        var (reconciler, provider) = Over(world);
        using var disposable = provider;

        await Assert.ThrowsAsync<DbUpdateException>(() => { return reconciler.ReconcileAsync(default); });
    }

    /// <summary>A row stuck installing whose task is long finished is settled by what the host has, and carries no task afterwards.</summary>
    [Theory]
    [InlineData(true, AppState.Installed)]
    [InlineData(false, AppState.Failed)]
    public async Task A_stuck_installing_row_is_settled_and_loses_its_task(bool hostHasIt, AppState expected)
    {
        using var world = new AppsWorld();
        Seed(world, "php-8.2", AppState.Installing);
        world.Agent.Installed = Result<IReadOnlyList<AgentApp>>.Ok(
            hostHasIt
                ? [new AgentApp("php-8.2", AgentAppKind.Runtime, "8.2", AgentAppServiceStatus.None)]
                : []);
        var (reconciler, provider) = Over(world);
        using var disposable = provider;

        Assert.True(await reconciler.ReconcileAsync(default));

        var row = await world.Db.InstalledApps.SingleAsync();
        Assert.Equal(expected, row.State);
        Assert.Null(row.TaskId);
    }

    /// <summary>The migration resolves rows that already hold two web server places before it creates the index that forbids them.</summary>
    [Fact]
    public void The_migration_resolves_duplicate_web_servers_before_it_creates_the_index()
    {
        var operations = new AppLedgerConcurrency().UpOperations.ToList();

        var resolve = operations.FindIndex(operation =>
        {
            return operation is SqlOperation sql && sql.Sql == AppLedgerConcurrency.ResolveDuplicateWebServers;
        });
        var index = operations.FindIndex(operation =>
        {
            return operation is CreateIndexOperation create && create.Name == "IX_InstalledApps_ActiveWebServer";
        });

        Assert.True(resolve >= 0, "the duplicate-resolving statement is missing");
        Assert.True(index > resolve, "the index must be created after the duplicates are resolved");
        Assert.Contains("LIMIT 1", AppLedgerConcurrency.ResolveDuplicateWebServers, StringComparison.Ordinal);
        Assert.Contains("\"State\" = 3", AppLedgerConcurrency.ResolveDuplicateWebServers, StringComparison.Ordinal);
    }
}
