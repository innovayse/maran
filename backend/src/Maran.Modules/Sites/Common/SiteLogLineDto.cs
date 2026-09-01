namespace Maran.Modules.Sites.Common;

/// <summary>The payload of one <c>line</c> event on the site-log stream.</summary>
/// <param name="Line">The log line as the agent read it, without its trailing newline.</param>
/// <param name="Historical">
/// True for a line replayed from the tail of the existing file, false for one appended while the
/// caller was watching. The SPA renders the two differently, so the operator can see where the
/// replay stops and the live log begins.
/// </param>
public sealed record SiteLogLineDto(string Line, bool Historical);
