namespace Maran.Agent.Client.Services.TransferService;

/// <summary>One event of an artifact export: the signed manifest, a slice of the artifact, or the way the export ended.</summary>
/// <remarks>
/// <para>
/// One of exactly four cases, each its own type: the header (<see cref="AgentBackupExportHeader"/>), a chunk
/// (<see cref="AgentBackupExportChunk"/>) and the two endings (<see cref="AgentBackupExportDone"/>,
/// <see cref="AgentBackupExportFailed"/>).
/// </para>
/// <para>
/// <b>Every sequence ends in exactly one ending.</b> A stream that stops without the agent stating an
/// outcome, or whose transport broke, ends in <see cref="AgentBackupExportFailed"/>, never in a quiet end of
/// the sequence: an artifact missing its tail must not be mistaken for a complete one.
/// </para>
/// </remarks>
public abstract record AgentBackupExportEvent
{
    /// <summary>Closes the hierarchy to the four cases this client declares.</summary>
    private protected AgentBackupExportEvent()
    {
    }
}
