namespace Maran.Agent.Client.Services.TransferService;

/// <summary>Something the agent could not read or could not validate; the entry was skipped, never repaired.</summary>
/// <param name="Warning">What it concerns.</param>
public sealed record AgentSourceInventoryWarningRaised(AgentSourceWarning Warning) : AgentSourceInventoryEvent;
