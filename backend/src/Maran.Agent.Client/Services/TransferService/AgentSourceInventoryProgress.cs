namespace Maran.Agent.Client.Services.TransferService;

/// <summary>A step the read has reached.</summary>
/// <param name="Step">The step; a closed set the panel renders from.</param>
public sealed record AgentSourceInventoryProgress(SourceInventoryStep Step) : AgentSourceInventoryEvent;
