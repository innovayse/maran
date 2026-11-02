namespace Maran.Agent.Client.Services.BackupService;

/// <summary>
/// Something wearing an artifact's name is not a regular file. Listed rather than skipped: a
/// directory is an entry retention can never prune, and a symbolic link is one a restore would
/// read through.
/// </summary>
public sealed record AgentBackupNotARegularFile : AgentBackupUnreadableReason;
