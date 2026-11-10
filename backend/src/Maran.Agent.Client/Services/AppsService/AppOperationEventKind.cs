namespace Maran.Agent.Client.Services.AppsService;

/// <summary>
/// Why an install or uninstall stream produced this event, and, for the six terminal kinds, how it ended.
/// </summary>
/// <remarks>
/// A stream that stops without a terminal message leaves the panel not knowing whether the packages
/// landed or went. That case gets its own kind rather than being folded into "the enumeration
/// finished", so a caller cannot mistake a truncated operation for a completed one.
/// </remarks>
public enum AppOperationEventKind
{
    /// <summary>Progress while the operation runs. Not terminal.</summary>
    Progress = 0,

    /// <summary>The operation finished: the app is installed (install) or gone (uninstall).</summary>
    Completed = 1,

    /// <summary>
    /// The agent dropped the stream because this client stopped reading it. The outcome is unknown;
    /// both operations are idempotent, so reopening the stream re-reports it.
    /// </summary>
    Dropped = 2,

    /// <summary>The agent closed the stream after its maximum idle time, with no outcome sent.</summary>
    Idle = 3,

    /// <summary>The operation failed; the event carries the typed reason.</summary>
    Failed = 4,

    /// <summary>The stream ended with no terminal message at all. The outcome is unknown, never a success.</summary>
    Truncated = 5,

    /// <summary>The caller cancelled the stream. The operation itself may still be running on the server.</summary>
    Cancelled = 6,
}
