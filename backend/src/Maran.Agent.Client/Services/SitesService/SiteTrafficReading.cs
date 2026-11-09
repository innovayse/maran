namespace Maran.Agent.Client.Services.SitesService;

/// <summary>One summarising read of a site's access log: what was counted, and where the next read starts.</summary>
/// <param name="Days">The deltas read since the cursor, one per UTC day.</param>
/// <param name="Cursor">Where the next read starts.</param>
/// <param name="CaughtUp">False when the agent's per-call budget stopped the read before the end of the log.</param>
/// <param name="Gap">True when the file the request's cursor named is gone, so the lines between are lost.</param>
/// <param name="UnparsedLines">How many lines the agent refused to count (too long, or malformed).</param>
public sealed record SiteTrafficReading(
    IReadOnlyList<SiteTrafficDayReading> Days,
    SiteTrafficCursor Cursor,
    bool CaughtUp,
    bool Gap,
    uint UnparsedLines);
