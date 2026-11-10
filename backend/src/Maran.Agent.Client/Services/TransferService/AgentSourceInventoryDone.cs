namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The terminal ok: the agent states the read is complete.</summary>
/// <param name="AccountCount">How many accounts the agent says it sent.</param>
/// <param name="WarningCount">How many warnings the agent says it sent.</param>
public sealed record AgentSourceInventoryDone(int AccountCount, int WarningCount) : AgentSourceInventoryEvent;
