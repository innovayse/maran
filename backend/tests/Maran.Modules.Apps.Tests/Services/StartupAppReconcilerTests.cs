using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Services;
using Maran.Modules.Apps.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
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
}
