namespace Maran.Agent.Client.Services.SitesService;

/// <summary>The operation itself failed; the last event of the sequence.</summary>
/// <param name="ErrorCode">
/// The machine-stable error code. It is a code and never the agent's own sentence, which can name
/// paths on the host.
/// </param>
public sealed record SiteLogFailed(string ErrorCode) : SiteLogEvent;
