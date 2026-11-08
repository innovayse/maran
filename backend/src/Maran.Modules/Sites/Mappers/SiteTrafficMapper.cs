using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Services;

namespace Maran.Modules.Sites.Mappers;

/// <summary>Restates a site's stored daily traffic rows as the figures its Traffic tab shows.</summary>
/// <remarks>
/// It sums and groups, and decides nothing about what the window is or whether the data is trusted: the
/// window, the day range and the incomplete-since cut-off arrive already worked out by the handler.
/// </remarks>
public static class SiteTrafficMapper
{
    /// <summary>Builds the traffic answer for one window.</summary>
    /// <param name="days">The window's length in days.</param>
    /// <param name="from">The first UTC day of the window.</param>
    /// <param name="to">The last UTC day of the window.</param>
    /// <param name="rows">The stored day rows inside the window, in any order.</param>
    /// <param name="incompleteSince">The UTC day lines were last lost, already cut to the window; null for none.</param>
    /// <param name="updatedAt">When the log was last read, or null when it never has been.</param>
    /// <param name="names">Names the status classes in the request's culture.</param>
    /// <returns>The answer, with an entry for every day of the window.</returns>
    public static SiteTrafficDto ToDto(
        int days,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<SiteTrafficDay> rows,
        DateOnly? incompleteSince,
        DateTimeOffset? updatedAt,
        SiteDisplayNames names)
    {
        var byDate = rows.ToDictionary(row => { return row.Date; });

        var perDay = new List<SiteTrafficDayDto>(days);
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            perDay.Add(byDate.TryGetValue(date, out var row)
                ? new SiteTrafficDayDto(date, row.Requests, row.BodyBytes, StatusClassesOf(row, names))
                : new SiteTrafficDayDto(date, 0, 0, []));
        }

        var statusCodes = rows
            .SelectMany(row => { return row.Statuses; })
            .GroupBy(status => { return status.Code; })
            .OrderBy(group => { return group.Key; })
            .Select(group => { return new SiteTrafficStatusDto(group.Key, group.Sum(status => { return status.Requests; })); })
            .ToList();

        var topPaths = rows
            .SelectMany(row => { return row.Paths; })
            .GroupBy(path => { return path.Path; }, StringComparer.Ordinal)
            .Select(group => { return new SiteTrafficPathDto(group.Key, group.Sum(path => { return path.Requests; })); })
            .OrderByDescending(path => { return path.Requests; })
            .ThenBy(path => { return path.Path; }, StringComparer.Ordinal)
            .Take(TrafficHistory.MaximumPaths)
            .ToList();

        return new SiteTrafficDto(
            days,
            from,
            to,
            rows.Count > 0,
            new SiteTrafficTotalsDto(
                rows.Sum(row => { return row.Requests; }),
                rows.Sum(row => { return row.BodyBytes; })),
            perDay,
            statusCodes,
            topPaths,
            incompleteSince,
            updatedAt);
    }

    /// <summary>Groups one day's status codes into their hundreds, oldest class first.</summary>
    /// <param name="row">The day.</param>
    /// <param name="names">Names the classes in the request's culture.</param>
    /// <returns>One entry per class that has requests.</returns>
    private static List<SiteTrafficStatusClassDto> StatusClassesOf(SiteTrafficDay row, SiteDisplayNames names)
    {
        return row.Statuses
            .GroupBy(status => { return status.Code / 100; })
            .OrderBy(group => { return group.Key; })
            .Select(group =>
            {
                return new SiteTrafficStatusClassDto(
                    names.OfStatusClass(group.Key),
                    group.Sum(status => { return status.Requests; }));
            })
            .ToList();
    }
}
