namespace Maran.Modules.Sites.Controllers.Requests;

/// <summary>The query of <c>GET /api/v1/sites/{id}/logs</c>.</summary>
/// <remarks>
/// Neither value has a forgiving default. An absent <c>source</c> is refused rather than guessed:
/// showing the error log to a caller who asked for the access log, or the reverse, is a wrong answer
/// delivered confidently. An absent history count replays nothing, which is the only count that
/// cannot mislead — the pane then fills only with lines written while it was open.
/// </remarks>
public sealed record TailSiteLogRequest
{
    /// <summary>Which log to read: <c>access</c> or <c>error</c>. Any other value is refused.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>How many existing lines to replay before the live ones. Bounded by the service.</summary>
    public int HistoryLines { get; init; }
}
