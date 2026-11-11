using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Commands.ControlAppService;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Services;
using Maran.Modules.Apps.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Apps.Tests.Commands.ControlAppService;

/// <summary>Controlling a service: the agent is asked, every attempt is journalled, a runtime without a service is refused.</summary>
public sealed class ControlAppServiceCommandHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The doubles.</param>
    /// <returns>The handler.</returns>
    private static ControlAppServiceCommandHandler HandlerOver(AppsWorld world)
    {
        return new ControlAppServiceCommandHandler(world.Agent, world.Journal, new AppDisplayNames(new KeyLocalizer()));
    }

    /// <summary>The action reaches the agent and the status after it is the answer.</summary>
    [Fact]
    public async Task The_action_reaches_the_agent_and_the_status_is_answered()
    {
        using var world = new AppsWorld();
        world.Agent.Control = Result<AgentAppServiceStatus>.Ok(AgentAppServiceStatus.Inactive);

        var result = await HandlerOver(world).HandleAsync(
            new ControlAppServiceCommand("nginx", AppServiceAction.Stop, "203.0.113.5", "ua"),
            default);

        Assert.Equal(AppServiceStatus.Inactive, result.Value.ServiceStatus);
        Assert.Equal(("nginx", AgentAppServiceAction.Stop), Assert.Single(world.Agent.ControlCalls));
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal("AppServiceControlled", entry.Action);
        Assert.Equal("nginx;action=stop", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The agent's refusal is passed through typed and journalled as a failure.</summary>
    [Fact]
    public async Task The_agents_refusal_is_passed_through_and_journalled()
    {
        using var world = new AppsWorld();
        world.Agent.Control = Result<AgentAppServiceStatus>.Fail(
            Error.Of("AgentAppNotInstalled", ErrorType.NotFound));

        var result = await HandlerOver(world).HandleAsync(
            new ControlAppServiceCommand("apache", AppServiceAction.Start),
            default);

        Assert.Equal("AgentAppNotInstalled", result.Error!.Code);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>A Node.js runtime has no service, so the agent is not asked.</summary>
    [Fact]
    public async Task A_node_runtime_is_refused_without_asking_the_agent()
    {
        using var world = new AppsWorld();

        var result = await HandlerOver(world).HandleAsync(
            new ControlAppServiceCommand("nodejs-22", AppServiceAction.Restart),
            default);

        Assert.Equal("AppsHasNoService", result.Error!.Code);
        Assert.Empty(world.Agent.ControlCalls);
    }
}
