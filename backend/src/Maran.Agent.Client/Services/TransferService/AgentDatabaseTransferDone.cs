namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The transfer finished.</summary>
/// <param name="DumpBytes">How many bytes of dump were received and loaded.</param>
public sealed record AgentDatabaseTransferDone(long DumpBytes) : AgentDatabaseTransferEvent;
