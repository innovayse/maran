namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Why a create stream produced this event — and, for the six terminal kinds, how it ended.</summary>
/// <remarks>
/// A create stream that stops without a terminal message leaves the panel not knowing whether an
/// artifact exists. That case gets its own kind rather than being folded into "the enumeration
/// finished", so a caller cannot write a completed backup row over a truncated stream.
/// </remarks>
public enum BackupCreateEventKind
{
    /// <summary>Progress while the backup runs. Not terminal.</summary>
    Progress = 0,

    /// <summary>The artifact was written; the size, digest and database count are on the event.</summary>
    Created = 1,

    /// <summary>
    /// The agent dropped the stream because this client stopped reading it. The backup's outcome is
    /// unknown; creating is idempotent on the backup id, so re-opening re-reports it.
    /// </summary>
    Dropped = 2,

    /// <summary>The agent closed the stream after its maximum idle time, with no outcome sent.</summary>
    Idle = 3,

    /// <summary>The backup failed; <c>ErrorCode</c> carries the typed reason.</summary>
    Failed = 4,

    /// <summary>
    /// The stream ended with no terminal message at all — a transport-level truncation. The outcome
    /// is unknown and must not be recorded as success.
    /// </summary>
    Truncated = 5,

    /// <summary>
    /// The caller cancelled. The backup may still be running on the server; creating is idempotent
    /// on the backup id, so re-opening the stream re-reports its outcome.
    /// </summary>
    Cancelled = 6,
}
