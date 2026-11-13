namespace Maran.Agent.Client.Services.AppsService;

/// <summary>What the agent offers for installation, and why the Node.js apps are missing when they are.</summary>
/// <param name="Apps">The installable apps in catalogue order, installed ones included.</param>
/// <param name="NodejsUnavailable">
/// Why the Node.js apps that are not installed are absent from <paramref name="Apps"/>, or <see langword="null"/>
/// when they are not absent for a reason (the host offers them, or its family does not use module streams, or
/// the agent states none).
/// </param>
public sealed record AgentInstallableApps(
    IReadOnlyList<AgentInstallableApp> Apps,
    AgentNodejsUnavailableReason? NodejsUnavailable);
