namespace Maran.Modules.Monitoring.Queries.GetClockSync;

/// <summary>Reads whether the host's clock is synchronised with a time source, live from the agent.</summary>
/// <remarks>
/// Takes no parameters: there is one clock, and the rpc behind it accepts nothing from a caller.
/// </remarks>
public sealed record GetClockSyncQuery;
