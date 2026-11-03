namespace Maran.Modules.Backups.Domain.Enums;

/// <summary>What the panel knows about whether a backup's artifact was read back and found sound.</summary>
/// <remarks>
/// <para>
/// <b>This is its own fact, never a refinement of <see cref="BackupStatus"/>.</b> A run that ended
/// <see cref="BackupStatus.Completed"/> stays completed when a later read of its artifact fails: the
/// run did what it reported, and the artifact is what turned out not to be restorable. Folding the
/// two into one status would make a failed verification rewrite the history of the run.
/// </para>
/// <para>
/// Three values, persisted by NAME. <see cref="NotVerified"/> is the default and says only that no
/// verification has answered yet; it is not a finding about the artifact.
/// </para>
/// </remarks>
public enum BackupVerificationState
{
    /// <summary>No verification has answered for this backup: it is unknown, not suspect.</summary>
    NotVerified = 0,

    /// <summary>The artifact was read back and matched what the run recorded.</summary>
    Verified = 1,

    /// <summary>The artifact was read back, or could not be, and was found unsound. A code says why.</summary>
    Failed = 2,
}
