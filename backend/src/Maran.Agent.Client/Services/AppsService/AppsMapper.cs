using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.AppsService;

/// <summary>
/// Reads the wire's app enums and messages into the panel's own mirror. It sits beside its client
/// because the generated wire types may not leave this project, so no module's Mappers/ can hold it.
/// </summary>
internal static class AppsMapper
{
    /// <summary>Reads a wire kind; "unspecified" becomes <see cref="AgentAppKind.Unknown"/> once, here.</summary>
    /// <param name="kind">The wire kind.</param>
    /// <returns>The panel's kind.</returns>
    public static AgentAppKind ToKind(AppKind kind)
    {
        return kind switch
        {
            AppKind.WebServer => AgentAppKind.WebServer,
            AppKind.Runtime => AgentAppKind.Runtime,
            _ => AgentAppKind.Unknown,
        };
    }

    /// <summary>Reads a wire service status; "unspecified" becomes <see cref="AgentAppServiceStatus.Unknown"/>.</summary>
    /// <param name="status">The wire status.</param>
    /// <returns>The panel's status.</returns>
    public static AgentAppServiceStatus ToServiceStatus(Maran.Agent.V1.AppServiceStatus status)
    {
        return status switch
        {
            Maran.Agent.V1.AppServiceStatus.Active => AgentAppServiceStatus.Active,
            Maran.Agent.V1.AppServiceStatus.Inactive => AgentAppServiceStatus.Inactive,
            Maran.Agent.V1.AppServiceStatus.Failed => AgentAppServiceStatus.Failed,
            Maran.Agent.V1.AppServiceStatus.None => AgentAppServiceStatus.None,
            _ => AgentAppServiceStatus.Unknown,
        };
    }

    /// <summary>Writes the panel's action as the wire's.</summary>
    /// <param name="action">The panel's action.</param>
    /// <returns>The wire action.</returns>
    public static Maran.Agent.V1.AppServiceAction ToWireAction(AgentAppServiceAction action)
    {
        return action switch
        {
            AgentAppServiceAction.Start => Maran.Agent.V1.AppServiceAction.Start,
            AgentAppServiceAction.Stop => Maran.Agent.V1.AppServiceAction.Stop,
            AgentAppServiceAction.Restart => Maran.Agent.V1.AppServiceAction.Restart,
            AgentAppServiceAction.Reload => Maran.Agent.V1.AppServiceAction.Reload,
            _ => Maran.Agent.V1.AppServiceAction.Unspecified,
        };
    }

    /// <summary>Reads the installed-app list of the wire.</summary>
    /// <param name="ok">The success payload of <c>ListApps</c>.</param>
    /// <returns>The apps in the order the agent sent them.</returns>
    public static List<AgentApp> ToApps(ListAppsOk ok)
    {
        var apps = new List<AgentApp>(ok.Apps.Count);
        foreach (var app in ok.Apps)
        {
            apps.Add(new AgentApp(app.AppId, ToKind(app.Kind), app.Version, ToServiceStatus(app.ServiceStatus)));
        }

        return apps;
    }

    /// <summary>Reads the installable-app list of the wire.</summary>
    /// <param name="ok">The success payload of <c>ListInstallableApps</c>.</param>
    /// <returns>The apps in catalogue order, installed ones included.</returns>
    public static List<AgentInstallableApp> ToInstallable(ListInstallableAppsOk ok)
    {
        var apps = new List<AgentInstallableApp>(ok.Apps.Count);
        foreach (var app in ok.Apps)
        {
            apps.Add(new AgentInstallableApp(app.AppId, ToKind(app.Kind), app.Installed));
        }

        return apps;
    }
}
