namespace Maran.Modules.Sites.Common;

/// <summary>One UTC day of a site's traffic.</summary>
/// <param name="Date">The UTC calendar day, as <c>yyyy-MM-dd</c>.</param>
/// <param name="Requests">How many requests the day holds.</param>
/// <param name="Bytes">How many response body bytes they sent.</param>
/// <param name="StatusClasses">The requests per status class, in class order.</param>
public sealed record SiteTrafficDayDto(
    DateOnly Date,
    long Requests,
    long Bytes,
    IReadOnlyList<SiteTrafficStatusClassDto> StatusClasses);
