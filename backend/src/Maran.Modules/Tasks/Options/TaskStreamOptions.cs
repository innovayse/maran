using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Tasks.Options;

/// <summary>Settings of the task stream, validated at startup.</summary>
public sealed class TaskStreamOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Tasks:Stream";

    /// <summary>
    /// How long the stream may go without writing anything before it sends a keep-alive comment.
    /// </summary>
    /// <remarks>
    /// It must stay comfortably below the read timeout of every proxy in front of the panel. The
    /// installer's nginx vhost sets <c>proxy_read_timeout</c> explicitly, but a customer's own proxy
    /// or a load balancer may impose one we never see — so the default is short enough (15 s) to
    /// survive the common 60 s default with room to spare, and long enough that a task nobody is
    /// reporting on costs four small writes a minute.
    ///
    /// The ceiling of 30 is derived rather than chosen: a heartbeat is only worth anything while it
    /// is shorter than the shortest read timeout in front of the panel, and the shortest one the
    /// panel can name is nginx's own 60-second default — so 30 is the largest value that still
    /// survives an unconfigured proxy, with one missed beat of margin.
    /// </remarks>
    [Range(1, 30)]
    public int HeartbeatSeconds { get; set; } = 15;

    /// <summary>
    /// How often the stream re-reads the task it is watching, in milliseconds.
    /// </summary>
    /// <remarks>
    /// The stream polls the row rather than being pushed to from the recorder, and that is a
    /// deliberate trade. A push would need a process-wide registry of live subscriptions that the
    /// recorder writes into while it is inside somebody else's account deletion — shared mutable
    /// state on the path of the most destructive operation the panel has, bought for a fraction of
    /// a second of latency on an admin-only progress bar. Polling has neither the state nor the
    /// lifetime problem, it survives a panel that one day runs as more than one process, and the
    /// row it reads is the same row the listing shows.
    ///
    /// The range's floor is what keeps the trade honest: below about a tenth of a second the query
    /// cost stops being negligible per open pane, and above two seconds a progress bar reads as
    /// stuck. The default is half a second — faster than an operator notices, slower than any
    /// operation reports.
    /// </remarks>
    [Range(100, 2000)]
    public int PollIntervalMilliseconds { get; set; } = 500;
}
