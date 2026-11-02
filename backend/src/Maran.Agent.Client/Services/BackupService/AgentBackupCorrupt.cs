namespace Maran.Agent.Client.Services.BackupService;

/// <summary>The artifact's sidecar was missing, or its JSON did not parse into any shape the agent writes.</summary>
public sealed record AgentBackupCorrupt : AgentBackupUnreadableReason;
