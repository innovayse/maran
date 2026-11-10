namespace Maran.Agent.Client.Services.TransferService;

/// <summary>An artifact the agent published under the source's backup id.</summary>
/// <param name="BackupId">The id the artifact is published under: the SOURCE backup id.</param>
/// <param name="ArtifactBytes">Artifact size in bytes.</param>
/// <param name="ArtifactSha256">Lowercase hexadecimal SHA-256 of the artifact.</param>
/// <param name="DatabaseCount">How many database dumps the artifact holds.</param>
/// <param name="AlreadyPresent">True when this very artifact was already published, so nothing was written.</param>
public sealed record AgentBackupImported(
    string BackupId,
    long ArtifactBytes,
    string ArtifactSha256,
    int DatabaseCount,
    bool AlreadyPresent) : AgentBackupImportResult;
