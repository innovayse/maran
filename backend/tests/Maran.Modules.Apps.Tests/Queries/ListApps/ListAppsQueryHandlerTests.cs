using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Queries.ListApps;
using Maran.Modules.Apps.Services;
using Maran.Modules.Apps.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Apps.Tests.Queries.ListApps;

/// <summary>Listing the apps: a row names a task only while its operation is in flight (issue #524).</summary>
public sealed class ListAppsQueryHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The doubles.</param>
    /// <returns>The handler.</returns>
    private static ListAppsQueryHandler HandlerOver(AppsWorld world)
    {
        return new ListAppsQueryHandler(world.Agent, world.Db, new AppDisplayNames(new KeyLocalizer()));
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

        var result = await HandlerOver(world).HandleAsync(new ListAppsQuery(), default);

        var tasks = result.Value.Items.ToDictionary(app => { return app.AppId; }, app => { return app.TaskId; });
        Assert.Equal(installTask, tasks["php-8.4"]);
        Assert.Equal(removeTask, tasks["php-8.3"]);
    }
}
