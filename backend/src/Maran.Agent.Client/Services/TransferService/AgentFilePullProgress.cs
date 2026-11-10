namespace Maran.Agent.Client.Services.TransferService;

/// <summary>A slice of the copy has ended and another will run.</summary>
/// <param name="SlicesCompleted">How many rsync runs have ended so far.</param>
public sealed record AgentFilePullProgress(int SlicesCompleted) : AgentFilePullEvent;
