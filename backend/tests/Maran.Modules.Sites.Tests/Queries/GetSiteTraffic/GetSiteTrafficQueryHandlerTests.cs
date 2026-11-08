using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Queries.GetSiteTraffic;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Queries.GetSiteTraffic;

/// <summary>Behavioral contract of <see cref="GetSiteTrafficQueryHandler"/>.</summary>
public sealed class GetSiteTrafficQueryHandlerTests
{
    /// <summary>The instant the handler believes it is: the 7th of November, 2026, UTC.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);

    /// <summary>A site that has never been read answers an empty first state, not a failure.</summary>
    [Fact]
    public async Task A_site_that_has_never_been_read_answers_an_empty_first_state()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context).HandleAsync(new GetSiteTrafficQuery(7, siteId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasData);
        Assert.Equal(0, result.Value.Totals.Requests);
        Assert.Equal(7, result.Value.PerDay.Count);
        Assert.Null(result.Value.UpdatedAt);
    }

    /// <summary>The window ends today and starts the requested number of days earlier, with every day listed.</summary>
    [Fact]
    public async Task The_window_ends_today_and_lists_every_day_including_quiet_ones()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, new DateOnly(2026, 11, 5), 40);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context).HandleAsync(new GetSiteTrafficQuery(7, siteId), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 11, 1), result.Value.From);
        Assert.Equal(new DateOnly(2026, 11, 7), result.Value.To);
        Assert.Equal(7, result.Value.PerDay.Count);
        Assert.True(result.Value.HasData);
        Assert.Equal(40, result.Value.Totals.Requests);
        Assert.Equal(40, result.Value.PerDay.Single(day => { return day.Date == new DateOnly(2026, 11, 5); }).Requests);
        Assert.Equal(0, result.Value.PerDay.Single(day => { return day.Date == new DateOnly(2026, 11, 6); }).Requests);
    }

    /// <summary>No window means the first the server lists.</summary>
    [Fact]
    public async Task No_window_means_the_first_the_server_lists()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context).HandleAsync(new GetSiteTrafficQuery(null, siteId), CancellationToken.None);

        Assert.Equal(7, result.Value.Days);
    }

    /// <summary>Days older than the window are not counted.</summary>
    [Fact]
    public async Task Days_older_than_the_window_are_not_counted()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, new DateOnly(2026, 10, 20), 99);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var week = await Handler(context).HandleAsync(new GetSiteTrafficQuery(7, siteId), CancellationToken.None);
        var month = await Handler(context).HandleAsync(new GetSiteTrafficQuery(30, siteId), CancellationToken.None);

        Assert.False(week.Value.HasData);
        Assert.Equal(99, month.Value.Totals.Requests);
    }

    /// <summary>A gap inside the window is reported.</summary>
    [Fact]
    public async Task A_gap_inside_the_window_is_reported()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(
            database, account, new DateOnly(2026, 11, 5), 1, gapAt: new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context).HandleAsync(new GetSiteTrafficQuery(7, siteId), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 11, 3), result.Value.IncompleteSince);
        Assert.Equal(Now, result.Value.UpdatedAt);
    }

    /// <summary>A gap from before the window is not reported, because it cannot have shortened what the window shows.</summary>
    [Fact]
    public async Task A_gap_from_before_the_window_is_not_reported()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(
            database, account, new DateOnly(2026, 11, 5), 1, gapAt: new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context).HandleAsync(new GetSiteTrafficQuery(7, siteId), CancellationToken.None);

        Assert.Null(result.Value.IncompleteSince);
    }

    /// <summary>Another tenant's site is answered not found, with no figure from it.</summary>
    [Fact]
    public async Task Another_tenants_site_is_answered_not_found()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, Guid.NewGuid(), new DateOnly(2026, 11, 5), 40);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var result = await Handler(context).HandleAsync(new GetSiteTrafficQuery(7, theirSiteId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
    }

    /// <summary>Status codes are summed over the window and a day's classes are named in the caller's language.</summary>
    [Fact]
    public async Task Status_codes_are_summed_over_the_window_and_classes_are_named()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, new DateOnly(2026, 11, 5), 40);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context).HandleAsync(new GetSiteTrafficQuery(7, siteId), CancellationToken.None);

        Assert.Equal([200, 404], result.Value.StatusCodes.Select(status => { return status.Code; }));
        var day = result.Value.PerDay.Single(entry => { return entry.Date == new DateOnly(2026, 11, 5); });
        Assert.Equal(["2xx success", "4xx client error"], day.StatusClasses.Select(item => { return item.Label; }));
        Assert.Equal("/index.php", Assert.Single(result.Value.TopPaths).Path);
    }

    /// <summary>Builds the handler under test at the fixed instant.</summary>
    /// <param name="context">The context bound to the calling principal.</param>
    /// <returns>The handler.</returns>
    private static GetSiteTrafficQueryHandler Handler(SitesDbContext context)
    {
        return new GetSiteTrafficQueryHandler(context, new FakeClock(Now), SitesTestContext.SiteNames());
    }

    /// <summary>Stores one site, optionally with one stored day and a cursor.</summary>
    /// <param name="database">The in-memory database name.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="day">The date of the one stored day, or null for none.</param>
    /// <param name="requests">The requests that day holds.</param>
    /// <param name="gapAt">When the cursor last saw lost lines, or null for never.</param>
    /// <returns>The site's id.</returns>
    private static async Task<Guid> SeedAsync(
        string database,
        Guid accountId,
        DateOnly? day = null,
        long requests = 0,
        DateTimeOffset? gapAt = null)
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(accountId, "example.com");
        context.Sites.Add(site);

        if (day is { } date)
        {
            var stored = new SiteTrafficDay(site, date);
            stored.Absorb(new TrafficDelta(
                requests,
                requests * 100,
                [new TrafficStatusCount(200, requests - 1), new TrafficStatusCount(404, 1)],
                [new TrafficPathCount("/index.php", requests)],
                0));
            context.SiteTrafficDays.Add(stored);
        }

        if (gapAt is { } lostAt)
        {
            var cursor = new SiteTrafficCursor(site, "1:2:abc", 10, Now);
            cursor.RecordGap(lostAt);
            context.SiteTrafficCursors.Add(cursor);
        }

        await context.SaveChangesAsync();

        return site.Id;
    }
}
