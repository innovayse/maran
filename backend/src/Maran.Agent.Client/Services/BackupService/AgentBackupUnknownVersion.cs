namespace Maran.Agent.Client.Services.BackupService;

/// <summary>The artifact's sidecar parsed but named a version the agent will not read past.</summary>
/// <param name="Version">
/// The version the sidecar named; zero when the sidecar predates the version field, which is a fact
/// about the sidecar and not an absent value.
/// </param>
public sealed record AgentBackupUnknownVersion(uint Version) : AgentBackupUnreadableReason;
