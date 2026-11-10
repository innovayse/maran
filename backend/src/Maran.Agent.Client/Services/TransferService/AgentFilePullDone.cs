namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The pull finished.</summary>
/// <param name="Slices">How many rsync runs it took.</param>
/// <param name="SourceBytes">The size of the source home measured at the start of the pull, or <c>null</c> when it could not be read.</param>
public sealed record AgentFilePullDone(int Slices, long? SourceBytes) : AgentFilePullEvent;
