using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Domain.ValueObjects;

namespace Maran.Modules.Sites.Domain.Entities;

/// <summary>What a site's access log counted on one UTC day: requests, bytes, status codes and top paths.</summary>
/// <remarks>
/// <para>
/// A day's figures arrive in several reads (the pass runs hourly and a busy day is split by the agent's
/// per-call budget), so a row is never written whole: <see cref="Absorb"/> adds a read's counts to it.
/// The totals (<see cref="Requests"/>, <see cref="BodyBytes"/>) are exact. The path list is the sum of
/// the reads' top lists capped at <see cref="TrafficHistory.MaximumPaths"/>, so the ranks beyond the
/// first few are approximate and a path that dropped out is counted under
/// <see cref="OtherPathRequests"/> rather than lost from the day.
/// </para>
/// <para>
/// Keyed by the site and the date, so a day exists once per site. Scoped to the owning account through
/// the required relationship to <see cref="Site"/> and not by a copy of the account id, so the two
/// cannot disagree (rules/security.md item 6). The row holds no visitor address: the agent never sends
/// one.
/// </para>
/// </remarks>
public sealed class SiteTrafficDay
{
    /// <summary>The site the figures belong to.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>The site, as the relationship EF Core cascades on and the tenant filter reads the account through.</summary>
    public Site Site { get; private set; }

    /// <summary>The UTC calendar day the figures were logged on.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>How many requests the day holds.</summary>
    public long Requests { get; private set; }

    /// <summary>How many response body bytes they sent.</summary>
    public long BodyBytes { get; private set; }

    /// <summary>The requests whose path is not among <see cref="Paths"/>.</summary>
    public long OtherPathRequests { get; private set; }

    /// <summary>The requests per HTTP status code, ordered by code.</summary>
    public IReadOnlyList<TrafficStatusCount> Statuses { get; private set; }

    /// <summary>The most requested paths of the day, highest first, at most <see cref="TrafficHistory.MaximumPaths"/>.</summary>
    public IReadOnlyList<TrafficPathCount> Paths { get; private set; }

    /// <summary>Creates an empty day for a site.</summary>
    /// <param name="site">The site the figures belong to.</param>
    /// <param name="date">The UTC day.</param>
    public SiteTrafficDay(Site site, DateOnly date)
    {
        Site = site;
        SiteId = site.Id;
        Date = date;
        Statuses = [];
        Paths = [];
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SiteTrafficDay()
    {
        Site = null!;
        Statuses = [];
        Paths = [];
    }

    /// <summary>Adds what one read counted to this day.</summary>
    /// <param name="delta">The counts of the read.</param>
    /// <remarks>
    /// Paths and status codes are merged by key and summed. The path list is then ordered by requests
    /// descending with the path itself breaking ties, so the same input always stores the same list,
    /// and cut to <see cref="TrafficHistory.MaximumPaths"/>; what the cut removes is added to
    /// <see cref="OtherPathRequests"/>. A status code outside the HTTP range is not a status and is
    /// left out of the per-code list, though its requests are still in <see cref="Requests"/>.
    /// </remarks>
    public void Absorb(TrafficDelta delta)
    {
        Requests += delta.Requests;
        BodyBytes += delta.BodyBytes;
        OtherPathRequests += delta.OtherPathRequests;

        var statuses = Statuses.ToDictionary(status => { return status.Code; }, status => { return status.Requests; });
        foreach (var status in delta.Statuses)
        {
            if (status.Code < TrafficHistory.FirstStatusCode || status.Code > TrafficHistory.LastStatusCode)
            {
                continue;
            }

            statuses[status.Code] = statuses.GetValueOrDefault(status.Code) + status.Requests;
        }

        Statuses = statuses
            .OrderBy(pair => { return pair.Key; })
            .Select(pair => { return new TrafficStatusCount(pair.Key, pair.Value); })
            .ToList();

        var paths = Paths.ToDictionary(path => { return path.Path; }, path => { return path.Requests; }, StringComparer.Ordinal);
        foreach (var path in delta.Paths)
        {
            paths[path.Path] = paths.GetValueOrDefault(path.Path) + path.Requests;
        }

        var ranked = paths
            .OrderByDescending(pair => { return pair.Value; })
            .ThenBy(pair => { return pair.Key; }, StringComparer.Ordinal)
            .ToList();

        Paths = ranked
            .Take(TrafficHistory.MaximumPaths)
            .Select(pair => { return new TrafficPathCount(pair.Key, pair.Value); })
            .ToList();

        // What the cap pushed out is still traffic for the day; keeping it in the "other" figure is
        // what lets the per-path numbers plus the other figure equal the total of path requests.
        OtherPathRequests += ranked.Skip(TrafficHistory.MaximumPaths).Sum(pair => { return pair.Value; });
    }
}
