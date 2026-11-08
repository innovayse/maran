namespace Maran.Modules.Sites.Common;

/// <summary>What a site's access log counted over a window of whole UTC days.</summary>
/// <param name="Days">The window's length in days, one of the lengths the choices endpoint lists.</param>
/// <param name="From">The first UTC day of the window, as <c>yyyy-MM-dd</c>.</param>
/// <param name="To">The last UTC day of the window, which is today.</param>
/// <param name="HasData">
/// False when no figure has been counted in the window yet, which is the ordinary first state of a new
/// site and not a failure.
/// </param>
/// <param name="Totals">The sums over the window.</param>
/// <param name="PerDay">One entry for every day of the window, days without traffic included, oldest first.</param>
/// <param name="StatusCodes">The requests per status code over the window, ordered by code.</param>
/// <param name="TopPaths">
/// The most requested paths, highest first. The sum of each day's top list, so ranks beyond the first few
/// are approximate.
/// </param>
/// <param name="IncompleteSince">
/// The UTC day on which lines were last lost, when that falls inside the window; null otherwise. The
/// figures may be short from that day on.
/// </param>
/// <param name="UpdatedAt">When the log was last read, or null when it never has been.</param>
public sealed record SiteTrafficDto(
    int Days,
    DateOnly From,
    DateOnly To,
    bool HasData,
    SiteTrafficTotalsDto Totals,
    IReadOnlyList<SiteTrafficDayDto> PerDay,
    IReadOnlyList<SiteTrafficStatusDto> StatusCodes,
    IReadOnlyList<SiteTrafficPathDto> TopPaths,
    DateOnly? IncompleteSince,
    DateTimeOffset? UpdatedAt);
