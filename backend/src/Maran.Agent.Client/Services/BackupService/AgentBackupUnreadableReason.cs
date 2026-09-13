namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Why one listed artifact carries no description.</summary>
/// <param name="Kind">Which of the reasons this is.</param>
/// <param name="Version">
/// The version the sidecar named, and only for <see cref="AgentBackupUnreadableKind.UnknownVersion"/>;
/// zero for every other kind, and zero there too when the sidecar predates the version field.
/// </param>
public sealed record AgentBackupUnreadableReason(AgentBackupUnreadableKind Kind, uint Version);
