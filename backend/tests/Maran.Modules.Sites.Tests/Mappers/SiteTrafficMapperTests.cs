using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Mappers;

/// <summary>Behavioral contract of <see cref="SiteTrafficMapper"/>.</summary>
public sealed class SiteTrafficMapperTests
{
    /// <summary>Paths of several days are summed by path, highest first, and cut to the stored maximum.</summary>
    [Fact]
    public void Paths_of_several_days_are_summed_by_path_highest_first_and_cut_to_the_maximum()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        var first = Day(site, new DateOnly(2026, 11, 5), [new TrafficPathCount("/a", 3), new TrafficPathCount("/b", 1)]);
        var second = Day(site, new DateOnly(2026, 11, 6), [new TrafficPathCount("/b", 5)]);

        var dto = SiteTrafficMapper.ToDto(
            7, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), [first, second], null, null, SitesTestContext.SiteNames());

        Assert.Equal([("/b", 6L), ("/a", 3L)], dto.TopPaths.Select(path => { return (path.Path, path.Requests); }));
        Assert.True(dto.TopPaths.Count <= TrafficHistory.MaximumPaths);
    }

    /// <summary>Every day of the window is listed, in order, whether or not it holds traffic.</summary>
    [Fact]
    public void Every_day_of_the_window_is_listed_in_order_whether_or_not_it_holds_traffic()
    {
        var dto = SiteTrafficMapper.ToDto(
            3, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 7), [], null, null, SitesTestContext.SiteNames());

        Assert.Equal(
            [new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 7)],
            dto.PerDay.Select(day => { return day.Date; }));
        Assert.False(dto.HasData);
    }

    /// <summary>Builds a day with the given paths.</summary>
    /// <param name="site">The site.</param>
    /// <param name="date">The day.</param>
    /// <param name="paths">The paths it counted.</param>
    /// <returns>The day.</returns>
    private static SiteTrafficDay Day(Site site, DateOnly date, IReadOnlyList<TrafficPathCount> paths)
    {
        var day = new SiteTrafficDay(site, date);
        day.Absorb(new TrafficDelta(paths.Sum(path => { return path.Requests; }), 0, [], paths, 0));

        return day;
    }
}
