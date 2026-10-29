namespace Maran.Host.HealthChecks;

/// <summary>
/// What <c>GET /health</c> reports: the panel's own state plus the pieces an operator needs to see
/// at a glance. Deliberately small — a health payload is read by monitoring and by people under
/// pressure, so it stays a flat, stable shape.
/// </summary>
/// <param name="Status">Panel status: <c>ok</c> when the process serves requests.</param>
/// <param name="Agent">
/// What the agent is to this panel: <c>connected</c>, <c>unavailable</c>, or <c>incompatible</c> when
/// it answered in a contract revision outside the window this panel drives
/// (<see cref="AgentHealthProbe"/>). Scripts match these words by value, so they are a contract.
/// </param>
/// <param name="Database">
/// Database reachability: <c>reachable</c>, <c>no_schema</c>, <c>unreachable</c> or
/// <c>not_configured</c> (<see cref="DatabaseHealthProbe"/>).
/// </param>
public sealed record PanelHealthReport(string Status, string Agent, string Database);
