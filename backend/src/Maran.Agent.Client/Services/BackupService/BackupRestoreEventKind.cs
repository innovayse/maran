namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Why a restore stream produced this event — and, for the six terminal kinds, how it ended.</summary>
/// <remarks>
/// <see cref="Restored"/> means the agent reported an outcome, NOT that the restore was whole. The
/// outcome on that event is what says how much of it happened, and a caller that treats the kind as
/// a verdict has made the mistake this contract's shape exists to prevent.
/// </remarks>
public enum BackupRestoreEventKind
{
    /// <summary>Progress while the restore runs. Not terminal.</summary>
    Progress = 0,

    /// <summary>The agent reported an outcome; read <c>Outcome</c> to learn what was actually restored.</summary>
    Restored = 1,

    /// <summary>The agent dropped the stream because this client stopped reading it.</summary>
    Dropped = 2,

    /// <summary>The agent closed the stream after its maximum idle time, with no outcome sent.</summary>
    Idle = 3,

    /// <summary>The restore failed; <c>ErrorCode</c> carries the typed reason.</summary>
    Failed = 4,

    /// <summary>
    /// The stream ended with no terminal message at all. What the account's home and databases now
    /// hold is unknown, which is the worst state this contract can report and must never be recorded
    /// as a completed restore.
    /// </summary>
    Truncated = 5,

    /// <summary>The caller cancelled. The restore may still be running on the server.</summary>
    Cancelled = 6,
}
