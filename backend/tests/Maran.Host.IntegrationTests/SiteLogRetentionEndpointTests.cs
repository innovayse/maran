using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The log retention endpoint over real HTTP against real PostgreSQL (issue #152), and the isolation test
/// the self-service matrix names for it: customer A changing customer B's site's retention is answered
/// not found, never forbidden, and changes nothing.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class SiteLogRetentionEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteLogRetentionEndpointTests(PostgresFixture postgres)
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

    /// <summary>A customer changing another tenant's site's retention is answered not found and never forbidden, and nothing changes.</summary>
    [Fact]
    public async Task A_customer_changing_another_tenants_retention_is_answered_not_found_and_nothing_changes()
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        var response = await client.PutAsJsonAsync($"/api/v1/sites/{world.StrangerSiteId}/log-retention", new { days = 3 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(14, await StoredRetentionAsync(world, world.StrangerSiteId));
    }

    /// <summary>A body that names another site's id changes only the site in the route.</summary>
    [Fact]
    public async Task A_body_that_names_another_sites_id_changes_only_the_site_in_the_route()
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/sites/{world.OwnSiteId}/log-retention",
            new { days = 3, siteId = world.StrangerSiteId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, await StoredRetentionAsync(world, world.OwnSiteId));
        Assert.Equal(14, await StoredRetentionAsync(world, world.StrangerSiteId));
    }

    /// <summary>A change persists, is answered with the site, and is journalled.</summary>
    [Fact]
    public async Task A_change_persists_is_answered_with_the_site_and_is_journalled()
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        var response = await client.PutAsJsonAsync($"/api/v1/sites/{world.OwnSiteId}/log-retention", new { days = 5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(5, body.RootElement.GetProperty("logRetentionDays").GetInt32());
        Assert.Equal(5, await StoredRetentionAsync(world, world.OwnSiteId));

        using var scope = world.Factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entry = await identity.AuditEvents.IgnoreQueryFilters().SingleAsync(item => item.Action == SiteAuditActions.SiteLogRetentionChanged);
        Assert.Equal(SiteLogsWorld.OwnDomain, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A number outside one to fourteen is refused with the retention code and stores nothing.</summary>
    /// <param name="days">The refused number.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(-1)]
    public async Task A_number_outside_one_to_fourteen_is_refused_and_stores_nothing(int days)
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");

        var response = await client.PutAsJsonAsync($"/api/v1/sites/{world.OwnSiteId}/log-retention", new { days });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SiteLogRetentionInvalid", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(14, await StoredRetentionAsync(world, world.OwnSiteId));
    }

    /// <summary>An anonymous caller is refused.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);
        using var client = world.Factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/v1/sites/{world.OwnSiteId}/log-retention", new { days = 3 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>The change is saved and answered while the agent does nothing: the request never waits on it.</summary>
    [Fact]
    public async Task The_change_is_saved_and_answered_without_the_request_reaching_the_agent()
    {
        await using var world = await SiteLogsWorld.CreateAsync(_pg);
        using var client = await EndpointHarness.SignInAsync(world.Factory, "customer");
        var before = world.Agent.Pruned.Count;

        var response = await client.PutAsJsonAsync($"/api/v1/sites/{world.OwnSiteId}/log-retention", new { days = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, world.Agent.Pruned.Count);
    }

    /// <summary>Reads a site's stored retention through a context that sees every account.</summary>
    /// <param name="world">The booted host.</param>
    /// <param name="siteId">The site.</param>
    /// <returns>The stored number of days.</returns>
    private static async Task<int> StoredRetentionAsync(SiteLogsWorld world, Guid siteId)
    {
        using var scope = world.Factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();

        return await sites.Sites.IgnoreQueryFilters().Where(site => site.Id == siteId)
            .Select(site => site.LogRetentionDays).SingleAsync();
    }
}
