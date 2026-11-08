using System.Globalization;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Usage;

namespace Maran.Agent.Simulated.Services.SitesService;

/// <summary>
/// What a simulated site's access log "holds": request counts, bytes, status codes and top paths per UTC
/// day, as a function of the domain, the date and the moment of the read, and of nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Each day has a total fixed by the domain and the date, and a read reports the part of that day the
/// read interval covers. The part is cut from a running share of the day's total, so reading in two
/// steps and reading in one report the same request count in all: a caller that adds up deltas, as the
/// real one does, is not fed drift. Within a single read the statuses and the paths add up to that
/// read's request count exactly.
/// </para>
/// <para>
/// The cursor is the second the last read reached, under a fixed file id. A cursor naming another file
/// is a log that was rotated away, so the read starts at the default window and reports the gap; so does
/// a cursor older than the window, whose lines are gone.
/// </para>
/// </remarks>
public static class SimulatedSiteTraffic
{
    /// <summary>The file id every cursor of a simulated access log carries.</summary>
    public const string AccessLogFileId = "access.log";

    /// <summary>The window a read starts at when it has no cursor, in days before now.</summary>
    private const int DefaultWindowDays = 2;

    /// <summary>The oldest a cursor may be before its lines count as rotated away, in days before now.</summary>
    private const int MaxWindowDays = 14;

    /// <summary>Seconds in a UTC day.</summary>
    private const long SecondsPerDay = 86400;

    /// <summary>The share of a day's requests that answer with each non-200 status, in percent.</summary>
    private static readonly (uint Code, ulong Percent)[] OtherStatuses = [(301, 3), (304, 10), (404, 7)];

    /// <summary>The paths a simulated site is requested at most, with their share of requests in percent.</summary>
    private static readonly (string Path, ulong Percent)[] TopPaths =
    [
        ("/", 30),
        ("/index.php", 20),
        ("/about", 10),
        ("/contact", 8),
        ("/blog/hello-world", 6),
    ];

    /// <summary>Reads the simulated access log of <paramref name="domain"/> from the cursor to now.</summary>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="cursor">Where the last read stopped, or null to start at the default window.</param>
    /// <param name="now">The moment the read is made at.</param>
    /// <returns>The per-day deltas and the cursor for the next read.</returns>
    public static SiteTrafficReading Read(string domain, SiteTrafficCursor? cursor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(domain);

        var nowSeconds = now.ToUnixTimeSeconds();
        var gap = false;
        long from;
        if (cursor is null)
        {
            from = nowSeconds - (DefaultWindowDays * SecondsPerDay);
        }
        else if (!string.Equals(cursor.FileId, AccessLogFileId, StringComparison.Ordinal))
        {
            from = nowSeconds - (DefaultWindowDays * SecondsPerDay);
            gap = true;
        }
        else
        {
            from = cursor.Offset > (ulong)nowSeconds ? nowSeconds : (long)cursor.Offset;
        }

        var oldest = nowSeconds - (MaxWindowDays * SecondsPerDay);
        if (from < oldest)
        {
            from = oldest;
            gap = true;
        }

        var days = new List<SiteTrafficDayReading>();
        for (var day = from / SecondsPerDay; day <= nowSeconds / SecondsPerDay; day++)
        {
            var reading = DayReading(domain, day, from, nowSeconds);
            if (reading is not null)
            {
                days.Add(reading);
            }
        }

        return new SiteTrafficReading(days, new SiteTrafficCursor(AccessLogFileId, (ulong)nowSeconds), true, gap, 0);
    }

    /// <summary>Reads the part of one day between <paramref name="from"/> and <paramref name="to"/>.</summary>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="day">The day, counted in days since the Unix epoch.</param>
    /// <param name="from">The second the read starts at.</param>
    /// <param name="to">The second the read ends at.</param>
    /// <returns>The day's delta, or null when the interval covers no request of it.</returns>
    private static SiteTrafficDayReading? DayReading(string domain, long day, long from, long to)
    {
        var dayStart = day * SecondsPerDay;
        var low = Math.Max(from, dayStart) - dayStart;
        var high = Math.Min(to, dayStart + SecondsPerDay) - dayStart;
        var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(dayStart).UtcDateTime);
        var seed = domain + "/" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var total = 200 + (StableHash.Of(seed) % 800);
        var requests = Share(total, high) - Share(total, low);
        if (requests == 0)
        {
            return null;
        }

        var averageBytes = 2000 + (StableHash.Of(seed + "/bytes") % 7000);

        return new SiteTrafficDayReading(
            date,
            requests,
            requests * averageBytes,
            Statuses(requests),
            Paths(requests, out var otherPaths),
            otherPaths);
    }

    /// <summary>The part of a day's total reached <paramref name="seconds"/> into the day.</summary>
    /// <param name="total">The day's total.</param>
    /// <param name="seconds">Seconds since the start of the day.</param>
    /// <returns>The running share, rounded down.</returns>
    private static ulong Share(ulong total, long seconds)
    {
        return total * (ulong)seconds / (ulong)SecondsPerDay;
    }

    /// <summary>Splits <paramref name="requests"/> over status codes so the counts add up to it exactly.</summary>
    /// <param name="requests">The requests in the read.</param>
    /// <returns>The non-zero counts, in ascending code order.</returns>
    private static List<SiteTrafficStatus> Statuses(ulong requests)
    {
        var counts = OtherStatuses.ToDictionary(
            status => { return status.Code; },
            status => { return requests * status.Percent / 100; });
        counts[200] = requests - counts.Values.Aggregate(0UL, (sum, count) => { return sum + count; });

        return counts
            .Where(pair => { return pair.Value > 0; })
            .OrderBy(pair => { return pair.Key; })
            .Select(pair => { return new SiteTrafficStatus(pair.Key, pair.Value); })
            .ToList();
    }

    /// <summary>Splits <paramref name="requests"/> over the top paths and the rest.</summary>
    /// <param name="requests">The requests in the read.</param>
    /// <param name="otherPaths">The requests no top path accounts for.</param>
    /// <returns>The non-zero top paths, most requested first.</returns>
    private static List<SiteTrafficPath> Paths(ulong requests, out ulong otherPaths)
    {
        var top = TopPaths
            .Select(path => { return new SiteTrafficPath(path.Path, requests * path.Percent / 100); })
            .Where(path => { return path.Requests > 0; })
            .ToList();
        otherPaths = requests - top.Aggregate(0UL, (sum, path) => { return sum + path.Requests; });

        return top;
    }
}
