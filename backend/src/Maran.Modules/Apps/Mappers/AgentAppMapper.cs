using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Domain.Enums;

namespace Maran.Modules.Apps.Mappers;

/// <summary>Restates the agent client's app enums as the module's own, once, so no handler switches on them.</summary>
public static class AgentAppMapper
{
    /// <summary>Restates a service status.</summary>
    /// <param name="status">The agent client's status.</param>
    /// <returns>The module's status; "not stated" stays <see cref="AppServiceStatus.Unknown"/>.</returns>
    public static AppServiceStatus ToServiceStatus(AgentAppServiceStatus status)
    {
        return status switch
        {
            AgentAppServiceStatus.Active => AppServiceStatus.Active,
            AgentAppServiceStatus.Inactive => AppServiceStatus.Inactive,
            AgentAppServiceStatus.Failed => AppServiceStatus.Failed,
            AgentAppServiceStatus.None => AppServiceStatus.None,
            _ => AppServiceStatus.Unknown,
        };
    }

    /// <summary>Restates an action for the agent client.</summary>
    /// <param name="action">The module's action.</param>
    /// <returns>The agent client's action.</returns>
    public static AgentAppServiceAction ToAgentAction(AppServiceAction action)
    {
        return action switch
        {
            AppServiceAction.Start => AgentAppServiceAction.Start,
            AppServiceAction.Stop => AgentAppServiceAction.Stop,
            AppServiceAction.Restart => AgentAppServiceAction.Restart,
            AppServiceAction.Reload => AgentAppServiceAction.Reload,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "AppServiceAction has four members."),
        };
    }
}
