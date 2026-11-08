using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The traffic summary endpoint over real HTTP against real PostgreSQL (issue #152), and the isolation
/// test the self-service matrix names for it: customer A asking for customer B's site's traffic is
/// answered not found, never forbidden.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class SiteTrafficEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteTrafficEndpointTests(PostgresFixture postgres)
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

    /// <summary>A customer asking for another tenant's site's traffic is answered not found and never forbidden.</summary>
    [Fact]
    public async Task A_customer_asking_for_another_tenants_traffic_is_answered_not_found_and_never_forbidden()
    {
        await using var world = await Seeded();
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        var response = await client.GetAsync($"/api/v1/sites/{world.StrangerSiteId}/traffic?days=7");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown site is answered not found, the same as another tenant's.</summary>
    [Fact]
    public async Task An_unknown_site_is_answered_not_found_the_same_as_another_tenants()
    {
        await using var world = await Seeded();
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        var response = await client.GetAsync($"/api/v1/sites/{Guid.NewGuid()}/traffic");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>An anonymous caller is refused.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        await using var world = await Seeded();
        using var client = world.Factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/sites/{world.OwnSiteId}/traffic");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer reading their own site with no figure yet is answered an empty first state.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_site_with_no_figure_yet_is_answered_an_empty_first_state()
    {
        await using var world = await Seeded();
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        var response = await client.GetAsync($"/api/v1/sites/{world.OwnSiteId}/traffic?days=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("hasData").GetBoolean());
        Assert.Equal(7, body.RootElement.GetProperty("perDay").GetArrayLength());
    }

    /// <summary>A customer reading their own site is answered its figures, and not the other tenant's.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_site_is_answered_its_figures_and_not_the_other_tenants()
    {
        await using var world = await Seeded();
        await StoreDayAsync(world.Factory, world.OwnSiteId, requests: 12);
        await StoreDayAsync(world.Factory, world.StrangerSiteId, requests: 900);
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        var response = await client.GetAsync($"/api/v1/sites/{world.OwnSiteId}/traffic?days=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("hasData").GetBoolean());
        Assert.Equal(12, body.RootElement.GetProperty("totals").GetProperty("requests").GetInt64());
        Assert.Equal("/index.php", body.RootElement.GetProperty("topPaths")[0].GetProperty("path").GetString());
    }

    /// <summary>A window the server does not list is refused with the window code.</summary>
    [Fact]
    public async Task A_window_the_server_does_not_list_is_refused_with_the_window_code()
    {
        await using var world = await Seeded();
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        var response = await client.GetAsync($"/api/v1/sites/{world.OwnSiteId}/traffic?days=8");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SiteTrafficWindowInvalid", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>A customer request never reaches the agent: the figures are read from the database alone.</summary>
    [Fact]
    public async Task A_customer_request_never_reaches_the_agent()
    {
        await using var world = await Seeded();
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        await client.GetAsync($"/api/v1/sites/{world.OwnSiteId}/traffic?days=30");

        Assert.Empty(world.Agent.Summarised);
        Assert.Empty(world.Agent.Pruned);
    }

    /// <summary>The choices endpoint lists the windows and retentions the SPA offers.</summary>
    [Fact]
    public async Task The_choices_endpoint_lists_the_windows_and_retentions()
    {
        await using var world = await Seeded();
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        var response = await client.GetAsync("/api/v1/sites/choices");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, body.RootElement.GetProperty("trafficWindows").GetArrayLength());
        Assert.Equal(14, body.RootElement.GetProperty("logRetentions").GetArrayLength());
    }

    /// <summary>Stores one day of traffic for a site.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="siteId">The site.</param>
    /// <param name="requests">The requests the day holds.</param>
    private static async Task StoreDayAsync(
        WebApplicationFactory<Program> factory,
        Guid siteId,
        long requests)
    {
        using var scope = factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var today = DateOnly.FromDateTime(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow.UtcDateTime);

        var site = await sites.Sites.IgnoreQueryFilters().SingleAsync(stored => stored.Id == siteId);
        var day = new SiteTrafficDay(site, today);
        day.Absorb(new TrafficDelta(
            requests, requests * 100, [new TrafficStatusCount(200, requests)], [new TrafficPathCount("/index.php", requests)], 0));
        sites.SiteTrafficDays.Add(day);
        await sites.SaveChangesAsync();
    }

    /// <summary>Boots the world this class's tests read from.</summary>
    private Task<SiteLogsWorld> Seeded()
    {
        return SiteLogsWorld.CreateAsync(_pg);
    }
}
