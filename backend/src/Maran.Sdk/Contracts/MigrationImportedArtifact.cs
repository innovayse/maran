namespace Maran.Sdk.Contracts;

/// <summary>A backup artifact the agent published on this host for an import, before any account exists for it.</summary>
/// <param name="BackupId">The id the artifact is published under: the id it had on the sending server.</param>
/// <param name="SizeBytes">The artifact's size.</param>
/// <param name="Sha256">The lowercase hexadecimal SHA-256 of the artifact, as the signed manifest declared it.</param>
/// <param name="DatabaseCount">How many database dumps the artifact holds.</param>
public sealed record MigrationImportedArtifact(Guid BackupId, long SizeBytes, string Sha256, int DatabaseCount);
