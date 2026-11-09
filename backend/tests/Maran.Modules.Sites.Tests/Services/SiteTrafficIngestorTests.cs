using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using AgentCursor = Maran.Agent.Client.Services.SitesService.SiteTrafficCursor;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>Behavioral contract of <see cref="SiteTrafficIngestor"/>.</summary>
public sealed class SiteTrafficIngestorTests
{
    /// <summary>The instant the ingestor believes it is.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The day the readings count.</summary>
    private static readonly DateOnly Day = new(2026, 11, 6);

    /// <summary>A first read stores the day, and a cursor, with no cursor sent to the agent.</summary>
    [Fact]
    public async Task A_first_read_stores_the_day_and_a_cursor_with_none_sent_to_the_agent()
    {
        var world = await WorldAsync();
        world.Agent.TrafficReadings.Enqueue(Reading(requests: 10, offset: 500));

        var result = await world.Ingest();

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(world.Agent.TrafficCursors));
        await using var reader = world.Reader();
        var day = await reader.SiteTrafficDays.SingleAsync();
        Assert.Equal(10, day.Requests);
        Assert.Equal(Day, day.Date);
        var cursor = await reader.SiteTrafficCursors.SingleAsync();
        Assert.Equal(("1:2:abc", 500L), (cursor.FileId, cursor.Offset));
        Assert.Equal(Now, cursor.UpdatedAt);
    }

    /// <summary>A second read adds to the day and sends the stored cursor.</summary>
    [Fact]
    public async Task A_second_read_adds_to_the_day_and_sends_the_stored_cursor()
    {
        var world = await WorldAsync();
        world.Agent.TrafficReadings.Enqueue(Reading(requests: 10, offset: 500));
        world.Agent.TrafficReadings.Enqueue(Reading(requests: 4, offset: 900));

        await world.Ingest();
        await world.Ingest();

        Assert.Equal(new AgentCursor("1:2:abc", 500), world.Agent.TrafficCursors[1]);
        await using var reader = world.Reader();
        Assert.Equal(14, (await reader.SiteTrafficDays.SingleAsync()).Requests);
        Assert.Equal(900, (await reader.SiteTrafficCursors.SingleAsync()).Offset);
    }

    /// <summary>A refused read writes nothing and keeps the old cursor, so the next pass re-reads.</summary>
    [Fact]
    public async Task A_refused_read_writes_nothing_and_keeps_the_old_cursor()
    {
        var world = await WorldAsync();
        world.Agent.TrafficReadings.Enqueue(Reading(requests: 10, offset: 500));
        await world.Ingest();
        world.Agent.TrafficReadings.Enqueue(Result<SiteTrafficReading>.Fail(Error.Of("AgentSaturated", ErrorType.Unavailable)));

        var result = await world.Ingest();

        Assert.Equal("AgentSaturated", result.Error!.Code);
        await using var reader = world.Reader();
        Assert.Equal(10, (await reader.SiteTrafficDays.SingleAsync()).Requests);
        Assert.Equal(500, (await reader.SiteTrafficCursors.SingleAsync()).Offset);
    }

    /// <summary>A failing write leaves the old cursor and no counts, so nothing is counted twice.</summary>
    [Fact]
    public async Task A_failing_write_leaves_the_old_cursor_and_no_counts()
    {
        var failing = new FailingSaveInterceptor();
        var world = await WorldAsync(failing);
        world.Agent.TrafficReadings.Enqueue(Reading(requests: 10, offset: 500));
        failing.Armed = true;

        await Assert.ThrowsAnyAsync<Exception>(() => { return world.Ingest(); });

        await using var reader = world.Reader();
        Assert.Empty(await reader.SiteTrafficDays.ToListAsync());
        Assert.Empty(await reader.SiteTrafficCursors.ToListAsync());
    }

    /// <summary>A reading that reports a gap records it on the cursor.</summary>
    [Fact]
    public async Task A_reading_that_reports_a_gap_records_it_on_the_cursor()
    {
        var world = await WorldAsync();
        world.Agent.TrafficReadings.Enqueue(Reading(requests: 1, offset: 10, gap: true));

        await world.Ingest();

        await using var reader = world.Reader();
        Assert.Equal(Now, (await reader.SiteTrafficCursors.SingleAsync()).IncompleteSince);
    }

    /// <summary>The figures land on the site that was asked about, whatever other sites exist.</summary>
    [Fact]
    public async Task The_figures_land_on_the_site_that_was_asked_about_and_no_other()
    {
        var world = await WorldAsync();
        var other = SitesTestContext.PhpSite(Guid.NewGuid(), "other.example");
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), world.Database))
        {
            seed.Sites.Add(other);
            await seed.SaveChangesAsync();
        }

        world.Agent.TrafficReadings.Enqueue(Reading(requests: 10, offset: 500));
        await world.Ingest();

        await using var reader = world.Reader();
        Assert.All(await reader.SiteTrafficDays.ToListAsync(), day => { Assert.Equal(world.SiteId, day.SiteId); });
        Assert.Equal(world.SiteId, (await reader.SiteTrafficCursors.SingleAsync()).SiteId);
    }

    /// <summary>Builds a successful reading of one day.</summary>
    /// <param name="requests">The requests counted.</param>
    /// <param name="offset">The offset the cursor ends at.</param>
    /// <param name="gap">Whether the agent reports lost lines.</param>
    /// <returns>The result the agent double answers with.</returns>
    private static Result<SiteTrafficReading> Reading(ulong requests, ulong offset, bool gap = false)
    {
        return Result<SiteTrafficReading>.Ok(new SiteTrafficReading(
            [new SiteTrafficDayReading(Day, requests, requests * 10, [new SiteTrafficStatus(200, requests)], [new SiteTrafficPath("/", requests)], 0)],
            new AgentCursor("1:2:abc", offset),
            true,
            gap,
            0));
    }

    /// <summary>Stores a site and builds the ingestor over an unattended context.</summary>
    /// <param name="failing">An interceptor that can fail the next save, or null.</param>
    /// <returns>The world the test drives.</returns>
    private static async Task<IngestWorld> WorldAsync(FailingSaveInterceptor? failing = null)
    {
        var database = Guid.NewGuid().ToString();
        var account = Guid.NewGuid();
        var site = SitesTestContext.PhpSite(account, "example.com");
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(site);
            await seed.SaveChangesAsync();
        }

        return new IngestWorld(database, site.Id, failing);
    }

    /// <summary>One site, one unattended context and the agent double the ingestor is driven through.</summary>
    private sealed class IngestWorld
    {
        /// <summary>The in-memory database name.</summary>
        public string Database { get; }

        /// <summary>The seeded site.</summary>
        public Guid SiteId { get; }

        /// <summary>The agent double.</summary>
        public RecordingAgentSitesClient Agent { get; } = new();

        /// <summary>The failing-save interceptor, or null.</summary>
        private readonly FailingSaveInterceptor? _failing;

        /// <summary>Creates the world.</summary>
        /// <param name="database">The in-memory database name.</param>
        /// <param name="siteId">The seeded site.</param>
        /// <param name="failing">An interceptor that can fail a save, or null.</param>
        public IngestWorld(string database, Guid siteId, FailingSaveInterceptor? failing)
        {
            Database = database;
            SiteId = siteId;
            _failing = failing;
        }

        /// <summary>Runs one ingest in a fresh unattended context, as a pass does.</summary>
        /// <returns>What the ingestor answered.</returns>
        public async Task<Result> Ingest()
        {
            await using var context = SitesTestContext.Create(FakeCurrentUser.Anonymous(), Database, _failing);
            var site = await context.Sites.IgnoreQueryFilters().SingleAsync(stored => stored.Id == SiteId);

            return await new SiteTrafficIngestor(context, Agent, new FakeClock(Now))
                .IngestAsync(site, "acme", CancellationToken.None);
        }

        /// <summary>Opens a context that sees every row, to read back what was stored.</summary>
        /// <returns>The context.</returns>
        public SitesDbContext Reader()
        {
            return SitesTestContext.Create(FakeCurrentUser.Admin(), Database);
        }
    }
}
