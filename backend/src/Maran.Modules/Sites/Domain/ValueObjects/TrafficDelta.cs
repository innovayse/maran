namespace Maran.Modules.Sites.Domain.ValueObjects;

/// <summary>What one summarising read counted for one UTC day, to be added to the day's stored row.</summary>
/// <param name="Requests">How many requests the read counted.</param>
/// <param name="BodyBytes">How many response body bytes they sent.</param>
/// <param name="Statuses">The requests per status code.</param>
/// <param name="Paths">The most requested paths of the read.</param>
/// <param name="OtherPathRequests">The requests whose path is not in <paramref name="Paths"/>.</param>
public sealed record TrafficDelta(
    long Requests,
    long BodyBytes,
    IReadOnlyList<TrafficStatusCount> Statuses,
    IReadOnlyList<TrafficPathCount> Paths,
    long OtherPathRequests);
