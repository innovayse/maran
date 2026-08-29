namespace Maran.Host.HealthChecks;

/// <summary>
/// What <c>GET /health</c> reports: the panel's own state plus the pieces an operator needs to see
/// at a glance. Deliberately small — a health payload is read by monitoring and by people under
/// pressure, so it stays a flat, stable shape.
/// </summary>
/// <param name="Status">Panel status: <c>ok</c> when the process serves requests.</param>
/// <param name="Agent">Agent connectivity: <c>connected</c> or <c>unavailable</c>.</param>
/// <param name="Database">Database reachability: <c>reachable</c>, <c>unreachable</c> or <c>not_configured</c>.</param>
public sealed record PanelHealthReport(string Status, string Agent, string Database);
