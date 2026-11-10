using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Two customers with one site each on a booted host whose agent is a double, for the endpoints and the
/// maintenance pass that read and set a site's logs (issue #152). The host never dials a real agent: a
/// request here that reached one would be work done as root on a developer's machine.
/// </summary>
/// <param name="Factory">The booted host.</param>
/// <param name="Agent">The agent double every site operation goes to.</param>
/// <param name="OwnSiteId">The site belonging to the signed-in customer <c>customer</c>.</param>
/// <param name="StrangerSiteId">The site belonging to the other tenant.</param>
public sealed record SiteLogsWorld(
    WebApplicationFactory<Program> Factory,
    RecordingAgentSitesClient Agent,
    Guid OwnSiteId,
    Guid StrangerSiteId) : IAsyncDisposable
{
    /// <summary>The primary domain of <see cref="OwnSiteId"/>.</summary>
    public const string OwnDomain = "own.example.com";

    /// <summary>The primary domain of <see cref="StrangerSiteId"/>.</summary>
    public const string StrangerDomain = "stranger.example.com";

    /// <summary>Boots the host, migrates every schema it touches and seeds the two customers and their sites.</summary>
    /// <param name="database">This test's database.</param>
    /// <returns>The world.</returns>
    public static async Task<SiteLogsWorld> CreateAsync(TestDatabase database)
    {
        var agent = new RecordingAgentSitesClient();
        var factory = EndpointHarness.CreateFactory(database, services =>
        {
            services.RemoveAll<IAgentSitesClient>();
            services.AddSingleton<IAgentSitesClient>(agent);
        });

        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        }

        var own = await EndpointHarness.SeedAccountAsync(factory, "own");
        var stranger = await EndpointHarness.SeedAccountAsync(factory, "stranger");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", own.AccountId);

        using var seedScope = factory.Services.CreateScope();
        var sites = seedScope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var now = seedScope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        // Written through the context resolved OUTSIDE a request, whose principal is not a signed-in
        // customer, so the seed itself does no tenant separating: the filter under test is the only thing
        // that can (rules/testing.md).
        var ownSite = NewSite(own.AccountId, OwnDomain, now);
        var strangerSite = NewSite(stranger.AccountId, StrangerDomain, now);
        sites.Sites.AddRange(ownSite, strangerSite);
        await sites.SaveChangesAsync();

        return new SiteLogsWorld(factory, agent, ownSite.Id, strangerSite.Id);
    }

    /// <summary>Shuts the booted host down.</summary>
    /// <returns>Resolves once the host has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
    }

    /// <summary>Builds one PHP-backed site row.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="now">The creation instant, from the panel's clock.</param>
    /// <returns>The site.</returns>
    private static Site NewSite(Guid accountId, string domain, DateTimeOffset now)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            [],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            $"/home/acct/sites/{domain}",
            now);
    }
}
