namespace Maran.Agent.Client.Services.SitesService;

/// <summary>
/// Why a tailed log stream produced this event — and, for the four terminal kinds, why it ended.
/// </summary>
/// <remarks>
/// The distinction is the point of the type. A log the operator is watching that simply stops
/// producing lines looks identical, at the call site, whether the agent closed it because nothing
/// was written, dropped it because the panel stopped reading, failed the operation, or reached its
/// natural end. Collapsing those into "the enumeration finished" is a silent truncation: the
/// operator keeps watching a pane that will never update again. Each ending therefore arrives as
/// its own final event.
/// </remarks>
public enum SiteLogEventKind
{
    /// <summary>One log line. Not terminal; more may follow.</summary>
    Line = 0,

    /// <summary>The agent closed the stream normally, with no further lines to send.</summary>
    Completed = 1,

    /// <summary>
    /// The agent dropped the stream because this client stopped reading it. Retryable immediately
    /// by reopening the stream.
    /// </summary>
    Dropped = 2,

    /// <summary>
    /// The agent closed the stream after its maximum idle time. Benign: nothing more was logged.
    /// </summary>
    Idle = 3,

    /// <summary>The operation itself failed; <c>ErrorCode</c> carries the typed reason.</summary>
    Failed = 4,

    /// <summary>
    /// The caller cancelled: the panel stopped watching. Terminal, and deliberately not
    /// <see cref="Completed"/> — an operator who closed the view must not leave behind a record
    /// saying the log ended of its own accord.
    /// </summary>
    Cancelled = 5,
}
