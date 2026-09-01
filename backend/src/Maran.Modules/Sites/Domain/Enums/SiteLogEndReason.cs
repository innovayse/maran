namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>
/// Why a tailed log stream ended. Exactly one of these reaches the browser as the stream's final
/// event, so a pane that has stopped updating always says why.
/// </summary>
/// <remarks>
/// The distinction is the whole reason the chain exists. The agent grew separate <c>STREAM_DROPPED</c>
/// and <c>STREAM_IDLE</c> endings, the agent client turned them into typed terminal events, and this
/// enum carries them the last hop to the operator. Collapsing them into "the stream closed" would
/// show a silent truncation dressed as a normal end — an operator would keep watching a log that
/// will never update again, believing they were seeing everything.
///
/// The names are the wire values, camel-cased by the serializer, and the SPA matches on them
/// (<c>frontend/src/types/siteLog.ts</c>). Renaming one is a contract change.
/// </remarks>
public enum SiteLogEndReason
{
    /// <summary>The agent closed the stream normally, with no further lines to send.</summary>
    Completed = 0,

    /// <summary>
    /// The agent dropped the stream because the panel stopped reading it fast enough. Lines were
    /// lost; reopening the stream is the remedy.
    /// </summary>
    Dropped = 1,

    /// <summary>The agent closed the stream after its maximum idle time. Nothing more was logged.</summary>
    Idle = 2,

    /// <summary>The operation itself failed; the end event carries the localized reason.</summary>
    Failed = 3,

    /// <summary>
    /// The stream stopped without the panel being told why — the agent's event sequence ended with
    /// no terminal event of its own. Deliberately pessimistic: lines may be missing, and saying
    /// "completed" here would be the exact false reassurance this enum exists to prevent.
    /// </summary>
    Truncated = 4,

    /// <summary>
    /// The caller stopped watching, and the tail was stopped with it. Not
    /// <see cref="Completed"/>: an operator who closed the view must not leave a record saying the
    /// log ended of its own accord.
    /// </summary>
    Cancelled = 5,
}
