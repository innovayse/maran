namespace Maran.Agent.Client.Services.PhpService;

/// <summary>
/// Why an extension-change stream produced this event — and, for the six terminal kinds, how it ended.
/// </summary>
/// <remarks>
/// A stream that stops without a terminal message leaves the panel not knowing whether the extension
/// changed. That case gets its own kind rather than being folded into "the enumeration finished", so a
/// caller cannot mistake a truncated change for a completed one.
/// </remarks>
public enum PhpExtensionEventKind
{
    /// <summary>Progress while the change runs. Not terminal.</summary>
    Progress = 0,

    /// <summary>The extension is in the requested state; the event echoes version, extension and state.</summary>
    Changed = 1,

    /// <summary>
    /// The agent dropped the stream because this client stopped reading it. The outcome is unknown;
    /// reopening the stream re-reports it, since switching an extension is idempotent.
    /// </summary>
    Dropped = 2,

    /// <summary>The agent closed the stream after its maximum idle time, with no outcome sent.</summary>
    Idle = 3,

    /// <summary>The change failed; the event's error carries the typed reason.</summary>
    Failed = 4,

    /// <summary>
    /// The stream ended with no terminal message at all — a transport-level truncation. The outcome
    /// is unknown and must not be reported as success.
    /// </summary>
    Truncated = 5,

    /// <summary>
    /// The caller cancelled the stream. The change itself may still be running on the server;
    /// switching an extension is idempotent, so reopening the stream re-reports its outcome.
    /// </summary>
    Cancelled = 6,
}
