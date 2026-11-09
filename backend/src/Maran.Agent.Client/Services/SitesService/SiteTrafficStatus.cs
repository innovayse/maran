namespace Maran.Agent.Client.Services.SitesService;

/// <summary>How many requests of one day were answered with one HTTP status code.</summary>
/// <param name="Code">The status code, as the agent counted it.</param>
/// <param name="Requests">How many requests were answered with it.</param>
public sealed record SiteTrafficStatus(uint Code, ulong Requests);
