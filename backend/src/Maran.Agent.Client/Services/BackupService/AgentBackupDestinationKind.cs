namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Where a backup artifact is stored.</summary>
/// <remarks>
/// Hand-written rather than the generated wire enum, because a generated type may not leave the
/// invoker layer (<c>WireTypeContainmentTests</c>). There is no unspecified member: the wire's zero
/// value exists only because proto3 requires one, and a panel-side kind that means "not stated" is a
/// value every caller would have to handle and no caller could act on.
/// </remarks>
public enum AgentBackupDestinationKind
{
    /// <summary>The agent's own root-only backup directory on this server.</summary>
    Local = 0,

    /// <summary>
    /// An S3-compatible object store. The agent that ships today REFUSES this kind at its service
    /// boundary with a not-implemented failure; it is never quietly served from the local directory
    /// instead, and the panel must show that refusal as itself.
    /// </summary>
    S3 = 1,
}
