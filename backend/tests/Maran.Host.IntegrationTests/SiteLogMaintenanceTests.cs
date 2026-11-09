using Maran.Agent.Client.Services.SitesService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Jobs;
using Maran.Modules.Sites.Persistence;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AgentCursor = Maran.Agent.Client.Services.SitesService.SiteTrafficCursor;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The hourly site log pass driven against real PostgreSQL with the agent as a double (issue #152): the
/// handler reads across accounts with no principal, reads before it prunes, survives a refused site, and
/// purges what the panel no longer keeps.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class SiteLogMaintenanceTests : IAsyncLifetime
{
    /// <summary>This test's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteLogMaintenanceTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>The pass reaches every account's sites although nobody is signed in, reading and pruning each.</summary>
    /// <remarks>The order between a site's read and its prune is held by the unit test of the maintainer, which sees one ordered call list.</remarks>
    [Fact]
    public async Task The_pass_reaches_every_accounts_sites_although_nobody_is_signed_in()
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);

        await RunPassAsync(world);

        Assert.Equal(
            [SiteLogsWorld.OwnDomain, SiteLogsWorld.StrangerDomain],
            world.Agent.Summarised.Order().ToList());
        Assert.Equal(
            [SiteLogsWorld.OwnDomain, SiteLogsWorld.StrangerDomain],
            world.Agent.Pruned.Select(pruned => { return pruned.Domain; }).Order().ToList());
    }

    /// <summary>Each site is pruned to its own stored retention.</summary>
    [Fact]
    public async Task Each_site_is_pruned_to_its_own_stored_retention()
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);
        using (var scope = world.Factory.Services.CreateScope())
        {
            var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
            (await sites.Sites.IgnoreQueryFilters().SingleAsync(site => site.Id == world.OwnSiteId)).ChangeLogRetention(3);
            await sites.SaveChangesAsync();
        }

        await RunPassAsync(world);

        Assert.Contains((SiteLogsWorld.OwnDomain, 3u), world.Agent.Pruned);
        Assert.Contains((SiteLogsWorld.StrangerDomain, 14u), world.Agent.Pruned);
    }

    /// <summary>A read is stored as the day's counts and a cursor, and a second pass continues from that cursor.</summary>
    [Fact]
    public async Task A_read_is_stored_as_counts_and_a_cursor_and_the_next_pass_continues_from_it()
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);
        world.Agent.TrafficReadings.Enqueue(Result<SiteTrafficReading>.Ok(new SiteTrafficReading(
            [new SiteTrafficDayReading(new DateOnly(2026, 11, 6), 8, 800, [new SiteTrafficStatus(200, 8)], [new SiteTrafficPath("/", 8)], 0)],
            new AgentCursor("1:2:abc", 700),
            true,
            false,
            0)));

        await RunPassAsync(world);

        using var scope = world.Factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var day = await sites.SiteTrafficDays.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(8, day.Requests);
        var cursor = await sites.SiteTrafficCursors.IgnoreQueryFilters().SingleAsync(item => item.SiteId == day.SiteId);
        Assert.Equal(700, cursor.Offset);
    }

    /// <summary>A site whose read was refused is not pruned, and the other site is still maintained.</summary>
    [Fact]
    public async Task A_site_whose_read_was_refused_is_not_pruned_and_the_other_site_is_still_maintained()
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);
        world.Agent.TrafficReadings.Enqueue(Result<SiteTrafficReading>.Fail(Error.Of("AgentSaturated", ErrorType.Unavailable)));

        await RunPassAsync(world);

        Assert.Equal(2, world.Agent.Summarised.Count);
        var pruned = Assert.Single(world.Agent.Pruned);
        Assert.NotEqual(world.Agent.Summarised[0], pruned.Domain);
    }

    /// <summary>Rows older than the panel keeps are purged and recent rows are kept.</summary>
    [Fact]
    public async Task Rows_older_than_the_panel_keeps_are_purged_and_recent_rows_are_kept()
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);
        using (var scope = world.Factory.Services.CreateScope())
        {
            var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
            var today = DateOnly.FromDateTime(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow.UtcDateTime);
            var site = await sites.Sites.IgnoreQueryFilters().SingleAsync(stored => stored.Id == world.OwnSiteId);
            foreach (var date in new[] { today.AddDays(-91), today.AddDays(-89), today })
            {
                var day = new SiteTrafficDay(site, date);
                day.Absorb(new TrafficDelta(1, 1, [], [], 0));
                sites.SiteTrafficDays.Add(day);
            }

            await sites.SaveChangesAsync();
        }

        await RunPassAsync(world);

        using var reader = world.Factory.Services.CreateScope();
        var remaining = await reader.ServiceProvider.GetRequiredService<SitesDbContext>()
            .SiteTrafficDays.IgnoreQueryFilters().CountAsync();
        Assert.Equal(2, remaining);
    }

    /// <summary>Runs one pass through the handler the way the scheduled message does.</summary>
    /// <param name="world">The booted host.</param>
    private static async Task RunPassAsync(SiteLogsWorld world)
    {
        using var scope = world.Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SiteLogMaintenanceHandler>();

        await handler.HandleAsync(new SiteLogMaintenanceRequested(), CancellationToken.None);
    }
}
