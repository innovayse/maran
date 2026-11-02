namespace Maran.Modules.Sites.Models;

/// <summary>A frame carrying one log line.</summary>
/// <param name="Line">The log line, without its trailing newline.</param>
/// <param name="Historical">Whether the line was replayed rather than appended live.</param>
public sealed record SiteLogLineFrame(string Line, bool Historical) : SiteLogFrame;
