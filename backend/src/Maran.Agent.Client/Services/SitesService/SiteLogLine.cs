namespace Maran.Agent.Client.Services.SitesService;

/// <summary>One log line. Not terminal; more events may follow.</summary>
/// <param name="Line">The raw log line without its trailing newline.</param>
/// <param name="Historical">
/// True for a line replayed from the existing tail, false for one appended while the caller was
/// watching.
/// </param>
public sealed record SiteLogLine(string Line, bool Historical) : SiteLogEvent;
