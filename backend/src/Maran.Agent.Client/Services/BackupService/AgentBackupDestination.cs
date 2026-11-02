namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Where a backup artifact is written to, or read back from.</summary>
/// <remarks>
/// <para>
/// One of exactly two cases, each its own type, because the two share nothing: a local destination
/// is the agent's own root-only directory and carries no member at all
/// (<see cref="AgentLocalBackupDestination"/>), while an object store carries a bucket, a region and
/// credentials (<see cref="AgentS3BackupDestination"/>). One flat record with seven S3 members that
/// had to be empty for a local destination, and a boolean that only meant something inside S3, let
/// a caller build a local destination that named a bucket — which the agent refuses — and a kind
/// that quietly collapsed to local for any value it did not know.
/// </para>
/// <para>
/// The wire message stays flat (a kind plus every member); the panel's cases are flattened onto it
/// in <c>AgentBackupClient</c>, the only place that knows the wire shape.
/// </para>
/// </remarks>
public abstract record AgentBackupDestination
{
    /// <summary>Closes the hierarchy to the two cases this client declares.</summary>
    private protected AgentBackupDestination()
    {
    }
}
