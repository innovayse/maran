namespace Maran.Agent.Client.Services.BackupService;

/// <summary>What the agent read back from a stored backup artifact when it verified it.</summary>
/// <remarks>
/// Every figure is a measurement of the artifact as it is on disk now, taken by reading it, never a
/// copy of what the panel recorded when the backup was created. A verification that failed carries
/// none of these: a corrupt, missing or changed artifact arrives as a failed result with a code, so a
/// zero here always means the artifact really holds none of that, and is never "nothing was read".
/// </remarks>
/// <param name="MemberCount">How many archive members the safe-member scan walked.</param>
/// <param name="DatabaseCount">How many databases the manifest names, each with its dump member present.</param>
/// <param name="ArtifactBytes">The size of the artifact file that was hashed and scanned.</param>
public sealed record AgentBackupVerification(uint MemberCount, uint DatabaseCount, ulong ArtifactBytes);
