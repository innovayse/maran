namespace Maran.Modules.Monitoring.Queries.ListServiceStatuses;

/// <summary>Reads whether each service the agent watches is up, down, or not known.</summary>
/// <remarks>
/// Takes no parameters. The set of units is closed and lives in the agent: no call anywhere accepts
/// a unit name from a caller, which is what keeps "tell me about a service" from becoming "tell me
/// about anything on this host".
/// </remarks>
public sealed record ListServiceStatusesQuery;
