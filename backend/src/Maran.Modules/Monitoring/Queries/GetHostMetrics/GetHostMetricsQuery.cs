namespace Maran.Modules.Monitoring.Queries.GetHostMetrics;

/// <summary>Reads the host's resource use right now, live from the agent.</summary>
/// <remarks>
/// Takes no parameters, and there is nothing it could take: the agent measures one machine and one
/// root filesystem, and no rpc in this area accepts a path, a device or an interface name from a
/// caller. That is a security property as much as a simplicity one — a read that named a path would
/// be a read a customer could point somewhere else.
/// </remarks>
public sealed record GetHostMetricsQuery;
