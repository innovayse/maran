namespace Maran.Agent.Client.Services.SitesService;

/// <summary>What the agent counted in a site's access log for one UTC day since the cursor it was given.</summary>
/// <param name="Date">The UTC calendar day the lines were logged on.</param>
/// <param name="Requests">How many requests the lines hold.</param>
/// <param name="BodyBytes">How many response body bytes they sent.</param>
/// <param name="Statuses">The requests per status code.</param>
/// <param name="Paths">The most requested paths, highest first.</param>
/// <param name="OtherPathRequests">The requests whose path is not in <paramref name="Paths"/>.</param>
public sealed record SiteTrafficDayReading(
    DateOnly Date,
    ulong Requests,
    ulong BodyBytes,
    IReadOnlyList<SiteTrafficStatus> Statuses,
    IReadOnlyList<SiteTrafficPath> Paths,
    ulong OtherPathRequests);
