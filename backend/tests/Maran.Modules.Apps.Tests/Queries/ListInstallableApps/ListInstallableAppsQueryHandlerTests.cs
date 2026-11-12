using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Queries.ListInstallableApps;
using Maran.Modules.Apps.Services;
using Maran.Modules.Apps.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Apps.Tests.Queries.ListInstallableApps;

/// <summary>The installable list says why Node.js is missing instead of omitting it silently (issue #532).</summary>
public sealed class ListInstallableAppsQueryHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The doubles.</param>
    /// <returns>The handler.</returns>
    private static ListInstallableAppsQueryHandler HandlerOver(AppsWorld world)
    {
        return new ListInstallableAppsQueryHandler(world.Agent, world.Db, new AppDisplayNames(new KeyLocalizer()));
    }

    /// <summary>Each reason the agent states reaches the answer as the module's own reason, beside the other apps.</summary>
    /// <param name="agentReason">The agent client's reason.</param>
    /// <param name="expected">The module's reason.</param>
    [Theory]
    [InlineData(AgentNodejsUnavailableReason.NoModuleStreams, NodejsUnavailableReason.NoModuleStreams)]
    [InlineData(AgentNodejsUnavailableReason.ListingFailed, NodejsUnavailableReason.ListingFailed)]
    public async Task A_missing_nodejs_carries_its_reason_beside_the_other_apps(
        AgentNodejsUnavailableReason agentReason,
        NodejsUnavailableReason expected)
    {
        using var world = new AppsWorld();
        world.Agent.Installable = Result<AgentInstallableApps>.Ok(
            new AgentInstallableApps([new AgentInstallableApp("nginx", AgentAppKind.WebServer, false)], agentReason));

        var result = await HandlerOver(world).HandleAsync(new ListInstallableAppsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.NodejsUnavailable);
        Assert.Equal("NodejsUnavailable" + expected, result.Value.NodejsUnavailableName);
        Assert.Equal("nginx", Assert.Single(result.Value.Apps).AppId);
    }

    /// <summary>The control: a host that lost nothing states no reason.</summary>
    [Fact]
    public async Task A_host_that_offers_nodejs_states_no_reason()
    {
        using var world = new AppsWorld();
        world.Agent.Installable = Result<AgentInstallableApps>.Ok(
            new AgentInstallableApps([new AgentInstallableApp("nodejs-22", AgentAppKind.Runtime, false)], null));

        var result = await HandlerOver(world).HandleAsync(new ListInstallableAppsQuery(), default);

        Assert.Null(result.Value.NodejsUnavailable);
        Assert.Null(result.Value.NodejsUnavailableName);
        Assert.Equal("nodejs-22", Assert.Single(result.Value.Apps).AppId);
    }
}
