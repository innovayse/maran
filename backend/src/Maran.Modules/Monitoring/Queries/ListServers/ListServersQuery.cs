namespace Maran.Modules.Monitoring.Queries.ListServers;

/// <summary>Lists the servers the panel manages, for the shell's server selector (issue #319).</summary>
/// <remarks>
/// Takes no parameters and is not paged: the list is the panel's own registry of servers, which holds
/// one entry until fleet mode (issue #180) adds a registry to read, and that issue gives it the
/// panel's paging contract along with the data that makes it long.
/// </remarks>
public sealed record ListServersQuery;
