namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The terminal event of a complete export.</summary>
/// <param name="ArtifactBytes">The artifact bytes sent; equals the manifest's declared size.</param>
public sealed record AgentBackupExportDone(long ArtifactBytes) : AgentBackupExportEvent;
