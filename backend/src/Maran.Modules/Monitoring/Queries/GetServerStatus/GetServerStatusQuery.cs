namespace Maran.Modules.Monitoring.Queries.GetServerStatus;

/// <summary>
/// Reads the server facts an API key holding the <c>server.read</c> scope may see: the pending update
/// counts and the reboot requirement, live from the agent (issue #144).
/// </summary>
/// <remarks>
/// Takes no parameters, and so has no validator: there is one host and nothing a caller could supply.
/// </remarks>
public sealed record GetServerStatusQuery;
