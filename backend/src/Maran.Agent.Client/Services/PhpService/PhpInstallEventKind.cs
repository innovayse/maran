namespace Maran.Agent.Client.Services.PhpService;

/// <summary>
/// Why an install stream produced this event — and, for the five terminal kinds, how it ended.
/// </summary>
/// <remarks>
/// An install stream that stops without a terminal message leaves the panel not knowing whether the
/// version was installed. That case gets its own kind rather than being folded into "the
/// enumeration finished", so a caller cannot mistake a truncated install for a completed one.
/// </remarks>
public enum PhpInstallEventKind
{
    /// <summary>Progress while the installation runs. Not terminal.</summary>
    Progress = 0,

    /// <summary>The version is installed; <c>Version</c> echoes what the agent installed.</summary>
    Installed = 1,

    /// <summary>
    /// The agent dropped the stream because this client stopped reading it. The install's outcome is
    /// unknown; reopening the stream re-reports it, since installing is idempotent.
    /// </summary>
    Dropped = 2,

    /// <summary>The agent closed the stream after its maximum idle time, with no outcome sent.</summary>
    Idle = 3,

    /// <summary>The install failed; <c>ErrorCode</c> carries the typed reason.</summary>
    Failed = 4,

    /// <summary>
    /// The stream ended with no terminal message at all — a transport-level truncation. The outcome
    /// is unknown and must not be reported as success.
    /// </summary>
    Truncated = 5,

    /// <summary>
    /// The caller cancelled the stream. The install itself may still be running on the server;
    /// installing is idempotent, so reopening the stream re-reports its outcome.
    /// </summary>
    Cancelled = 6,
}
