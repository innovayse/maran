namespace Maran.Modules.Backups.Models;

/// <summary>A create run that finished an artifact the agent named.</summary>
/// <param name="SizeBytes">The artifact's size.</param>
/// <param name="Sha256">The artifact's digest, hex and lowercase; never empty.</param>
/// <param name="DatabaseCount">How many dumps the archive holds.</param>
public sealed record CreatedBackupOutcome(long SizeBytes, string Sha256, int DatabaseCount) : BackupRunOutcome;
