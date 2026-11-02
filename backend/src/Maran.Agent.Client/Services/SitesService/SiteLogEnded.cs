namespace Maran.Agent.Client.Services.SitesService;

/// <summary>The tail ended in a way that is not a fault; the last event of the sequence.</summary>
/// <param name="Kind">Which of the four endings it was.</param>
public sealed record SiteLogEnded(SiteLogEndKind Kind) : SiteLogEvent;
