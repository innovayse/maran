namespace Maran.Modules.Monitoring.Queries.GetPendingUpdateCount;

/// <summary>
/// Reads how many package updates the server's package manager already knows are pending, live from
/// the agent. The figure behind the navigation badge (issue #324).
/// </summary>
/// <remarks>
/// Takes no parameters, and so has no validator: there is one host and the rpc behind it accepts
/// nothing from a caller, so there is no member whose bounds could need holding.
/// </remarks>
public sealed record GetPendingUpdateCountQuery;
