namespace Maran.Agent.Client.Services.AppsService;

/// <summary>One app the server's package sources can install.</summary>
/// <param name="AppId">The app id of the closed catalogue.</param>
/// <param name="Kind">What the app is for.</param>
/// <param name="Installed">Whether the app is already installed on the server.</param>
public sealed record AgentInstallableApp(string AppId, AgentAppKind Kind, bool Installed);
