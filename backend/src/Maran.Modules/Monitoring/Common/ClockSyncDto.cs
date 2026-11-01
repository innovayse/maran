using Maran.Agent.Client.Services.MonitorService;

namespace Maran.Modules.Monitoring.Common;

/// <summary>Whether the host's clock is synchronised with a time source (issue #150).</summary>
/// <remarks>
/// The state crosses as an enum member, so it serializes in the panel's one camelCase spelling like
/// every other state on this API; the explanation beside it is resolved by the backend in the
/// request's culture, because the SPA holds no text for a server-side fact (rules/architecture.md).
/// </remarks>
/// <param name="State">Synchronised, not synchronised, or no synchronisation service enabled.</param>
/// <param name="Description">What the state means for this server, in the request's culture.</param>
public sealed record ClockSyncDto(AgentClockSyncState State, string Description);
