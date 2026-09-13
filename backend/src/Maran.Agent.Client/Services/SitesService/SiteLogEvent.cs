namespace Maran.Agent.Client.Services.SitesService;

/// <summary>One event from a tailed site log: a line, or the reason the stream ended.</summary>
/// <param name="Kind">Whether this is a line or one of the four terminal endings.</param>
/// <param name="Line">The raw log line without its trailing newline; empty for terminal events.</param>
/// <param name="Historical">
/// True for lines replayed from the existing tail, false for lines appended live. Always false for
/// terminal events.
/// </param>
/// <param name="ErrorCode">
/// The machine-stable error code for <see cref="SiteLogEventKind.Failed"/>; null otherwise. It is a
/// code and never the agent's own sentence, which can name paths on the host.
/// </param>
public sealed record SiteLogEvent(SiteLogEventKind Kind, string Line, bool Historical, string? ErrorCode);
