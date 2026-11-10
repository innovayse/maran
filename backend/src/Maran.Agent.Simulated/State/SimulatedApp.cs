using Maran.Agent.Client.Services.AppsService;

namespace Maran.Agent.Simulated.State;

/// <summary>One app the simulated host has installed through the app store.</summary>
/// <param name="AppId">The catalogue id, the key the entry is held under.</param>
/// <param name="Kind">What the app is for.</param>
/// <param name="Version">The version the simulated host reports.</param>
/// <param name="ServiceStatus">What the simulated service manager reports.</param>
public sealed record SimulatedApp(
    string AppId,
    AgentAppKind Kind,
    string Version,
    AgentAppServiceStatus ServiceStatus);
