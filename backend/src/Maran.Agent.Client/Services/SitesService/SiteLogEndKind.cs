namespace Maran.Agent.Client.Services.SitesService;

/// <summary>Why a tailed log stream ended without a fault.</summary>
public enum SiteLogEndKind
{
    /// <summary>The agent closed the stream normally, with no further lines to send.</summary>
    Completed,

    /// <summary>
    /// The agent dropped the stream because this client stopped reading it. Retryable immediately
    /// by reopening the stream.
    /// </summary>
    Dropped,

    /// <summary>
    /// The agent closed the stream after its maximum idle time. Benign: nothing more was logged.
    /// </summary>
    Idle,

    /// <summary>
    /// The caller cancelled: the panel stopped watching. Terminal, and deliberately not
    /// <see cref="Completed"/> — an operator who closed the view must not leave behind a record
    /// saying the log ended of its own accord.
    /// </summary>
    Cancelled,
}
