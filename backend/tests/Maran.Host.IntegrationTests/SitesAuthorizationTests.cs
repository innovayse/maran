using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sites.Controllers;
using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The IDOR fixture the Sites surface is required to have (rules/testing.md, Definition of Done 3),
/// driven over real HTTP against real PostgreSQL: two customers, two sites, and the question asked
/// of every site-scoped route at once — does customer A reaching for customer B's site get 404, and
/// not 403.
/// </summary>
/// <remarks>
/// It has to be 404. A 403 is an answer that says "this site exists but is not yours", which is
/// exactly the fact an attacker wanted; iterating identifiers then enumerates every domain on the
/// server. The distinction is not made by a check in a handler — it is made by the tenant query
/// filter on <see cref="SitesDbContext"/>, which means the row genuinely is not in the result set.
///
/// The routes are enumerated in one place so that a new site-scoped endpoint added without a row
/// here is visible in review as a route with nothing proving it.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SitesAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route under <c>/api/v1/sites</c> that names one site. <c>{id}</c> is substituted per test.</summary>
    /// <remarks>
    /// The list must be COMPLETE, and its completeness is asserted by
    /// <see cref="Every_site_scoped_route_on_the_controller_is_covered_by_the_idor_fixture"/> rather
    /// than trusted. It was not complete when it was written — the PHP version rebind was missing,
    /// so a Forbidden-suffixed code in that handler survived this entire suite while the class
    /// documentation claimed every site-scoped route was asked the same question.
    /// </remarks>
    public static TheoryData<string, string> SiteScopedEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/sites/{id}" },
            { "GET", "/api/v1/sites/{id}/logs" },
            { "POST", "/api/v1/sites/{id}/php-version" },
            { "POST", "/api/v1/sites/{id}/enable" },
            { "POST", "/api/v1/sites/{id}/disable" },
            { "DELETE", "/api/v1/sites/{id}" },
        };
    }

    /// <summary>Every route under <c>/api/v1/sites</c>, including the collection ones.</summary>
    public static TheoryData<string, string> AllSiteEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/sites" },
            { "POST", "/api/v1/sites" },
            { "GET", "/api/v1/sites/php-versions" },
            { "GET", "/api/v1/sites/{id}" },
            { "GET", "/api/v1/sites/{id}/logs" },
            { "POST", "/api/v1/sites/{id}/php-version" },
            { "POST", "/api/v1/sites/{id}/enable" },
            { "POST", "/api/v1/sites/{id}/disable" },
            { "DELETE", "/api/v1/sites/{id}" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SitesAuthorizationTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>An anonymous caller is refused by every site endpoint.</summary>
    [Theory]
    [MemberData(nameof(AllSiteEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_site_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer reaching for another tenants site is answered not found and never forbidden.</summary>
    [Theory]
    [MemberData(nameof(SiteScopedEndpoints))]
    public async Task A_customer_reaching_for_another_tenants_site_is_answered_not_found_and_never_forbidden(
        string method,
        string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, world.StrangerSiteId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A customer reading their own site is answered with it.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_site_is_answered_with_it()
    {
        // Guards the theory above from passing for the wrong reason: if the route were simply
        // broken, or the seed never ran, "not found" would be true of every request.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/sites/{world.OwnSiteId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("own.example.com", body.RootElement.GetProperty("domain").GetString());
    }

    /// <summary>Listing sites shows a customer only their own.</summary>
    [Fact]
    public async Task Listing_sites_shows_a_customer_only_their_own()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/sites");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var domains = body.RootElement.EnumerateArray().Select(site =>
        {
            return site.GetProperty("domain").GetString();
        }).ToList();

        Assert.Equal(["own.example.com"], domains);
    }

    /// <summary>An unknown site identifier answers not found rather than failing.</summary>
    [Theory]
    [MemberData(nameof(SiteScopedEndpoints))]
    public async Task An_unknown_site_identifier_answers_not_found_rather_than_failing(string method, string path)
    {
        // 404, never 500: an identifier the caller invented is answered by the handler's typed
        // NotFound, not by an unhandled failure that reveals the shape of what is behind it.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Every site scoped route on the controller is covered by the idor fixture.</summary>
    /// <remarks>
    /// The fixture above is a hand-written list, and a hand-written list of routes goes stale the
    /// first time somebody adds a route. This reads the routes off <see cref="SitesController"/>
    /// itself, so a new site-scoped endpoint fails HERE — naming itself — rather than quietly
    /// enjoying no IDOR coverage while the class documentation claims otherwise.
    /// </remarks>
    [Fact]
    public void Every_site_scoped_route_on_the_controller_is_covered_by_the_idor_fixture()
    {
        var declared = ControllerRoutes.Declared<SitesController>();
        Assert.NotEmpty(declared);

        var scopedInFixture = RouteStrings(SiteScopedEndpoints());
        var allInFixture = RouteStrings(AllSiteEndpoints());

        var missingFromAll = declared.Where(route =>
        {
            return !allInFixture.Contains(route);
        }).ToList();

        var missingFromScoped = declared
            .Where(route =>
            {
                // Keyed on the route having a PARAMETER, not on one spelling of one
                // parameter name: a route added as {siteId:guid} is just as resource-scoped
                // and needs the same 404-never-403 proof.
                return ControllerRoutes.IsResourceScoped(route);
            })
            .Where(route =>
            {
                return !scopedInFixture.Contains(route);
            })
            .ToList();

        Assert.True(
            missingFromAll.Count == 0,
            "These SitesController routes are absent from AllSiteEndpoints(): " + string.Join(", ", missingFromAll));
        Assert.True(
            missingFromScoped.Count == 0,
            "These site-scoped routes are absent from SiteScopedEndpoints(), so nothing proves they "
            + "answer 404 rather than 403 for another tenant: " + string.Join(", ", missingFromScoped));
    }

    /// <summary>Flattens a theory's rows into "METHOD /path" strings.</summary>
    /// <param name="rows">One of the fixtures above.</param>
    private static HashSet<string> RouteStrings(TheoryData<string, string> rows)
    {
        return rows
            .Select(row =>
            {
                return $"{row[0]} {row[1]}";
            })
            .ToHashSet(StringComparer.Ordinal);
    }


    /// <summary>Substitutes a site id into a route template.</summary>
    /// <param name="path">The route template.</param>
    /// <param name="siteId">The identifier to place in it.</param>
    private static string Substitute(string path, Guid siteId)
    {
        return path.Replace("{id}", siteId.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);
        });
    }

    /// <summary>Applies every module's migrations, the way the installer does before first boot.</summary>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts, their users, and one site each.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifiers the tests address.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5));
        var own = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        var stranger = new Account(Guid.NewGuid(), "stranger", "stranger.example.com", planId, now);
        accounts.Accounts.AddRange(own, stranger);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(own.Id);
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        // Written through the context resolved OUTSIDE a request, whose ICurrentUser is not a
        // signed-in customer, so the seed itself does no tenant separating — the filter under test
        // is the only thing that can (rules/testing.md).
        var ownSite = NewSite(own.Id, "own.example.com", now);
        var strangerSite = NewSite(stranger.Id, "stranger.example.com", now);
        sites.Sites.AddRange(ownSite, strangerSite);
        await sites.SaveChangesAsync();

        return new SeededWorld(ownSite.Id, strangerSite.Id);
    }

    /// <summary>Builds one PHP-backed site row.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="now">The creation instant, from the panel's clock.</param>
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
            $"/home/acct/sites/{domain}",
            now);
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Issues one request, giving each POST route a body its model binder accepts.</summary>
    /// <remarks>
    /// The bodies must be VALID. A route whose body fails validation answers 400, which would make
    /// an authorization theory pass without the request ever reaching the handler whose tenant
    /// scoping is the thing under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = BodyFor(path);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Builds a valid request body for one POST route.</summary>
    /// <param name="path">The absolute path being posted to.</param>
    private static JsonContent BodyFor(string path)
    {
        if (path.EndsWith("/php-version", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { phpVersion = "8.3" });
        }

        if (path.EndsWith("/api/v1/sites", StringComparison.Ordinal))
        {
            return JsonContent.Create(new
            {
                accountId = Guid.NewGuid(),
                domain = "new.example.com",
                aliases = Array.Empty<string>(),
                backendType = 1,
                phpVersion = string.Empty,
                proxyUpstream = string.Empty,
            });
        }

        return JsonContent.Create(new { });
    }

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="OwnSiteId">The site belonging to the signed-in customer.</param>
    /// <param name="StrangerSiteId">The site belonging to the other tenant.</param>
    private sealed record SeededWorld(Guid OwnSiteId, Guid StrangerSiteId);
}
