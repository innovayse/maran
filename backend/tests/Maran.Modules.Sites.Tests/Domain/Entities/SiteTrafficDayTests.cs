using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Domain.Entities;

/// <summary>Behavioral contract of <see cref="SiteTrafficDay"/>, which adds each read's counts to a day.</summary>
public sealed class SiteTrafficDayTests
{
    /// <summary>The day under test.</summary>
    private static readonly DateOnly Day = new(2026, 11, 6);

    /// <summary>Two reads of one day add their totals.</summary>
    [Fact]
    public void Two_reads_of_one_day_add_their_totals()
    {
        var day = NewDay();

        day.Absorb(Delta(requests: 10, bytes: 1_000));
        day.Absorb(Delta(requests: 5, bytes: 250));

        Assert.Equal(15, day.Requests);
        Assert.Equal(1_250, day.BodyBytes);
    }

    /// <summary>Status codes are merged by code and ordered by code.</summary>
    [Fact]
    public void Status_codes_are_merged_by_code_and_ordered_by_code()
    {
        var day = NewDay();

        day.Absorb(Delta(statuses: [new TrafficStatusCount(404, 2), new TrafficStatusCount(200, 7)]));
        day.Absorb(Delta(statuses: [new TrafficStatusCount(200, 3)]));

        Assert.Equal([new TrafficStatusCount(200, 10), new TrafficStatusCount(404, 2)], day.Statuses);
    }

    /// <summary>A code outside the HTTP range is left out of the per-code list.</summary>
    [Fact]
    public void A_code_outside_the_http_range_is_left_out_of_the_per_code_list()
    {
        var day = NewDay();

        day.Absorb(Delta(requests: 3, statuses: [new TrafficStatusCount(99, 1), new TrafficStatusCount(600, 1), new TrafficStatusCount(500, 1)]));

        Assert.Equal([new TrafficStatusCount(500, 1)], day.Statuses);
        Assert.Equal(3, day.Requests);
    }

    /// <summary>Paths are merged by path and ordered by requests, a tie broken by the path itself.</summary>
    [Fact]
    public void Paths_are_merged_by_path_and_ordered_by_requests_with_ties_broken_by_the_path()
    {
        var day = NewDay();

        day.Absorb(Delta(paths: [new TrafficPathCount("/b", 2), new TrafficPathCount("/a", 2), new TrafficPathCount("/c", 9)]));
        day.Absorb(Delta(paths: [new TrafficPathCount("/b", 1)]));

        Assert.Equal(
            [new TrafficPathCount("/c", 9), new TrafficPathCount("/b", 3), new TrafficPathCount("/a", 2)],
            day.Paths);
    }

    /// <summary>The path list is capped, and what the cap removes is counted as other, so the sum still adds up.</summary>
    [Fact]
    public void The_path_list_is_capped_and_what_the_cap_removes_is_counted_as_other()
    {
        var day = NewDay();
        var paths = Enumerable.Range(1, TrafficHistory.MaximumPaths + 5)
            .Select(index => { return new TrafficPathCount($"/p{index:D3}", 1); })
            .ToList();

        day.Absorb(Delta(requests: paths.Count, paths: paths, other: 7));

        Assert.Equal(TrafficHistory.MaximumPaths, day.Paths.Count);
        Assert.Equal(12, day.OtherPathRequests);
        Assert.Equal(paths.Count, day.Paths.Sum(path => { return path.Requests; }) + day.OtherPathRequests - 7);
    }

    /// <summary>The same input always stores the same list, whatever order it arrives in.</summary>
    [Fact]
    public void The_same_input_always_stores_the_same_list_whatever_order_it_arrives_in()
    {
        var forward = NewDay();
        var backward = NewDay();
        var paths = new[] { new TrafficPathCount("/x", 1), new TrafficPathCount("/y", 1), new TrafficPathCount("/z", 1) };

        forward.Absorb(Delta(paths: paths));
        backward.Absorb(Delta(paths: paths.Reverse().ToList()));

        Assert.Equal(forward.Paths, backward.Paths);
    }

    /// <summary>A new day is empty.</summary>
    [Fact]
    public void A_new_day_is_empty()
    {
        var day = NewDay();

        Assert.Equal(0, day.Requests);
        Assert.Empty(day.Statuses);
        Assert.Empty(day.Paths);
        Assert.Equal(Day, day.Date);
    }

    /// <summary>Builds an empty day on a fresh site.</summary>
    /// <returns>The day.</returns>
    private static SiteTrafficDay NewDay()
    {
        return new SiteTrafficDay(SitesTestContext.PhpSite(Guid.NewGuid(), "example.com"), Day);
    }

    /// <summary>Builds a delta, defaulting every part that a test does not care about.</summary>
    /// <param name="requests">The requests counted.</param>
    /// <param name="bytes">The body bytes counted.</param>
    /// <param name="statuses">The per-code counts.</param>
    /// <param name="paths">The per-path counts.</param>
    /// <param name="other">The requests outside the paths.</param>
    /// <returns>The delta.</returns>
    private static TrafficDelta Delta(
        long requests = 0,
        long bytes = 0,
        IReadOnlyList<TrafficStatusCount>? statuses = null,
        IReadOnlyList<TrafficPathCount>? paths = null,
        long other = 0)
    {
        return new TrafficDelta(requests, bytes, statuses ?? [], paths ?? [], other);
    }
}
