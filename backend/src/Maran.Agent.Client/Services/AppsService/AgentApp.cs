namespace Maran.Agent.Client.Services.AppsService;

/// <summary>One app installed on the server, as the agent probes it.</summary>
/// <param name="AppId">
/// The app id of the closed catalogue: <c>nginx</c>, <c>apache</c>, <c>php-&lt;ver&gt;</c> or
/// <c>nodejs-&lt;major&gt;</c>.
/// </param>
/// <param name="Kind">What the app is for.</param>
/// <param name="Version">The version as the host reports it, or the empty string when it reports none.</param>
/// <param name="ServiceStatus">What the service manager reports about the app's service right now.</param>
public sealed record AgentApp(
    string AppId,
    AgentAppKind Kind,
    string Version,
    AgentAppServiceStatus ServiceStatus);
