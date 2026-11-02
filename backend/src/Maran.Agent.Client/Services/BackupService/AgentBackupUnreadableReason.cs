namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Why one listed artifact carries no description.</summary>
/// <remarks>
/// <para>
/// One of exactly three cases, each its own type, because they call for three different reactions:
/// a sidecar that will not parse is probably damage (<see cref="AgentBackupCorrupt"/>), one written
/// at a version this agent does not know is probably a routine skew
/// (<see cref="AgentBackupUnknownVersion"/>), and a directory or a symbolic link wearing an
/// artifact's name is neither (<see cref="AgentBackupNotARegularFile"/>).
/// </para>
/// <para>
/// Hand-written rather than the generated wire enum, because a generated type may not leave the
/// invoker layer (<c>WireTypeContainmentTests</c>). The wire's unspecified value, and a kind a newer
/// agent added, have no case here: <c>AgentBackupClient.ListAsync</c> refuses such a listing as an
/// invalid response rather than inventing a reason the agent never gave. The version belongs to the
/// one case it means something for, so no other case carries a zero standing in for "no version".
/// </para>
/// </remarks>
public abstract record AgentBackupUnreadableReason
{
    /// <summary>Closes the hierarchy to the three reasons this client declares.</summary>
    private protected AgentBackupUnreadableReason()
    {
    }
}
