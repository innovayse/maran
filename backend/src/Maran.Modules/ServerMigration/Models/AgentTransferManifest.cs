namespace Maran.Modules.ServerMigration.Models;

/// <summary>The figures of the agent's signed manifest that the panel acts on, read from the exact bytes the agent signed.</summary>
/// <param name="BackupId">The id the artifact was published under on the sending server.</param>
/// <param name="Account">The account the artifact belongs to.</param>
/// <param name="ArtifactBytes">The artifact's size; the reader stops after exactly this many bytes.</param>
/// <param name="ArtifactSha256">The artifact's lowercase hexadecimal SHA-256.</param>
/// <param name="HomeBytes">The size of the home directory in the artifact.</param>
/// <param name="DatabaseCount">How many database dumps the artifact holds.</param>
public sealed record AgentTransferManifest(
    Guid BackupId,
    string Account,
    long ArtifactBytes,
    string ArtifactSha256,
    long HomeBytes,
    int DatabaseCount);
