namespace Maran.Agent.Client.Services.TransferService;

/// <summary>One account found on the source, complete.</summary>
/// <param name="Account">The account with its sites, databases and counts.</param>
public sealed record AgentSourceInventoryAccountFound(AgentSourceAccount Account) : AgentSourceInventoryEvent;
