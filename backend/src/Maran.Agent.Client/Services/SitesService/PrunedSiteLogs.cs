namespace Maran.Agent.Client.Services.SitesService;

/// <summary>What removing a site's surplus rotated log archives did.</summary>
/// <param name="RemovedFiles">How many archives the agent removed; zero is the ordinary answer.</param>
public sealed record PrunedSiteLogs(uint RemovedFiles);
