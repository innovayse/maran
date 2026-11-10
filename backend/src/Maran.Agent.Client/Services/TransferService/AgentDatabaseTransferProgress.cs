namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The transfer has reached a step.</summary>
/// <param name="Step">The step.</param>
public sealed record AgentDatabaseTransferProgress(DatabaseTransferStep Step) : AgentDatabaseTransferEvent;
