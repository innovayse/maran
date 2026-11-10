namespace Maran.Sdk.Contracts;

/// <summary>A backup made to be exported, ready to be read out by the agent.</summary>
/// <param name="BackupId">The backup's id; the agent publishes and finds the artifact by it.</param>
/// <param name="SizeBytes">The artifact's size.</param>
/// <param name="Sha256">The lowercase hexadecimal SHA-256 of the artifact.</param>
/// <param name="DatabaseCount">How many database dumps the artifact holds.</param>
public sealed record MigrationExportArtifact(Guid BackupId, long SizeBytes, string Sha256, int DatabaseCount);
