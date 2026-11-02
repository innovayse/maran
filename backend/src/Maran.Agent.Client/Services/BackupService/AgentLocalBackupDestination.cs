namespace Maran.Agent.Client.Services.BackupService;

/// <summary>The agent's own root-only backup directory on this server.</summary>
/// <remarks>
/// It has no members: the local root is the agent's own, and the agent refuses a destination that
/// names a path or an S3 field rather than ignoring it. A type with nothing to set cannot be built
/// wrong.
/// </remarks>
public sealed record AgentLocalBackupDestination : AgentBackupDestination;
