using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Controllers;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The IDOR fixture the certificates surface is required to have (rules/testing.md, Definition of
/// Done 3), driven over real HTTP against real PostgreSQL: two customers, two certificates, and the
/// question asked of every certificate-scoped route at once — does customer A reaching for customer
/// B's certificate get 404, and not 403.
/// </summary>
/// <remarks>
/// It has to be 404, and the reason is sharper here than for sites. A certificate is issuance
/// authority over a name: a 403 tells an attacker that a given domain is hosted on this server by an
/// account they can see the boundary of, and iterating identifiers then enumerates the machine's
/// certificate inventory. The distinction is made by the tenant query filter on
/// <see cref="SslDbContext"/> and by the tenant-scoped site directory, not by a check in a handler.
///
/// The routes are enumerated in one place and the list's completeness is ASSERTED against the
/// controller rather than trusted, so a certificate-scoped endpoint added later fails here naming
/// itself instead of quietly enjoying no coverage.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class CertificatesAuthorizationTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Every route under <c>/api/v1/certificates</c> that names one certificate.</summary>
    public static TheoryData<string, string> CertificateScopedEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "DELETE", "/api/v1/certificates/{id}" },
        };
    }

    /// <summary>Every route under <c>/api/v1/certificates</c>, including the collection ones.</summary>
    public static TheoryData<string, string> AllCertificateEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/certificates" },
            { "POST", "/api/v1/certificates" },
            { "POST", "/api/v1/certificates/custom" },
            { "DELETE", "/api/v1/certificates/{id}" },
        };
    }

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public CertificatesAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every certificate endpoint.</summary>
    [Theory]
    [MemberData(nameof(AllCertificateEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_certificate_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer reaching for another tenants certificate is answered not found and never forbidden.</summary>
    [Theory]
    [MemberData(nameof(CertificateScopedEndpoints))]
    public async Task A_customer_reaching_for_another_tenants_certificate_is_answered_not_found_and_never_forbidden(
        string method,
        string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, world.StrangerCertificateId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown certificate identifier answers not found rather than failing.</summary>
    [Theory]
    [MemberData(nameof(CertificateScopedEndpoints))]
    public async Task An_unknown_certificate_identifier_answers_not_found_rather_than_failing(
        string method,
        string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Listing certificates shows a customer only their own.</summary>
    [Fact]
    public async Task Listing_certificates_shows_a_customer_only_their_own()
    {
        // Also guards the theories above from passing for the wrong reason: if the seed never ran,
        // "not found" would be true of every request and the list would be empty here.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/certificates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var domains = body.RootElement.EnumerateArray().Select(certificate =>
        {
            return certificate.GetProperty("domain").GetString();
        }).ToList();

        Assert.Equal(["own.example.com"], domains);
    }

    /// <summary>No certificate the api returns carries any material.</summary>
    [Fact]
    public async Task No_certificate_the_api_returns_carries_any_material()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var body = await (await client.GetAsync("/api/v1/certificates")).Content.ReadAsStringAsync();

        // A site's PHP runs as that customer, so an endpoint that returned a key would be an
        // endpoint any script on the site could call (rules/security.md item 8).
        Assert.DoesNotContain("PRIVATE KEY", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BEGIN CERTIFICATE", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Issuing for a domain the caller does not own is answered not found and never forbidden.</summary>
    [Fact]
    public async Task Issuing_for_a_domain_the_caller_does_not_own_is_answered_not_found_and_never_forbidden()
    {
        // The collection route takes a DOMAIN, not an id, so the tenant check it depends on is the
        // site directory's rather than the certificate context's — a separate path, asserted apart.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.PostAsJsonAsync(
            "/api/v1/certificates", new { domain = "stranger.example.com" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Installing custom material for a domain the caller does not own is answered not found.</summary>
    [Fact]
    public async Task Installing_custom_material_for_a_domain_the_caller_does_not_own_is_answered_not_found()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.PostAsJsonAsync("/api/v1/certificates/custom", new
        {
            domain = "stranger.example.com",
            certificatePem = "-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----",
            privateKeyPem = "-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Every certificate scoped route on the controller is covered by the idor fixture.</summary>
    /// <remarks>
    /// The fixtures above are hand-written lists, and a hand-written list of routes goes stale the
    /// first time somebody adds a route. This reads the routes off
    /// <see cref="CertificatesController"/> itself, so a new endpoint fails HERE — naming itself —
    /// rather than quietly enjoying no IDOR coverage.
    /// </remarks>
    [Fact]
    public void Every_certificate_scoped_route_on_the_controller_is_covered_by_the_idor_fixture()
    {
        var declared = ControllerRoutes.Declared<CertificatesController>();
        Assert.NotEmpty(declared);

        var scopedInFixture = RouteStrings(CertificateScopedEndpoints());
        var allInFixture = RouteStrings(AllCertificateEndpoints());

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
            "These CertificatesController routes are absent from AllCertificateEndpoints(): "
            + string.Join(", ", missingFromAll));
        Assert.True(
            missingFromScoped.Count == 0,
            "These certificate-scoped routes are absent from CertificateScopedEndpoints(), so nothing "
            + "proves they answer 404 rather than 403 for another tenant: " + string.Join(", ", missingFromScoped));
    }

    /// <summary>Flattens a theory's rows into "METHOD /path" strings.</summary>
    /// <param name="rows">One of the fixtures above.</param>
    /// <returns>The route strings.</returns>
    private static HashSet<string> RouteStrings(TheoryData<string, string> rows)
    {
        return rows
            .Select(row =>
            {
                return $"{row[0]} {row[1]}";
            })
            .ToHashSet(StringComparer.Ordinal);
    }


    /// <summary>Substitutes a certificate id into a route template.</summary>
    /// <param name="path">The route template.</param>
    /// <param name="certificateId">The identifier to place in it.</param>
    /// <returns>The concrete path.</returns>
    private static string Substitute(string path, Guid certificateId)
    {
        return path.Replace("{id}", certificateId.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
    /// <returns>The booted factory.</returns>
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

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>Applies every module's migrations, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SslDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts, their users, one site each, and one certificate each.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifiers the tests address.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var ssl = scope.ServiceProvider.GetRequiredService<SslDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
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

        // Written through contexts resolved OUTSIDE a request, whose ICurrentUser is not a signed-in
        // customer, so the seed itself does no tenant separating — the filter under test is the only
        // thing that can (rules/testing.md).
        var ownSite = NewSite(own.Id, "own.example.com", now);
        var strangerSite = NewSite(stranger.Id, "stranger.example.com", now);
        sites.Sites.AddRange(ownSite, strangerSite);
        await sites.SaveChangesAsync();

        var ownCertificate = NewCertificate(own.Id, ownSite.Id, "own.example.com", now);
        var strangerCertificate = NewCertificate(stranger.Id, strangerSite.Id, "stranger.example.com", now);
        ssl.Certificates.AddRange(ownCertificate, strangerCertificate);
        await ssl.SaveChangesAsync();

        return new SeededWorld(ownCertificate.Id, strangerCertificate.Id);
    }

    /// <summary>Builds one PHP-backed site row.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="now">The creation instant, from the panel's clock.</param>
    /// <returns>The site row.</returns>
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

    /// <summary>Builds one certificate row.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="siteId">The site it belongs to.</param>
    /// <param name="domain">The certificate's domain.</param>
    /// <param name="now">The issue instant, from the panel's clock.</param>
    /// <returns>The certificate row.</returns>
    private static Certificate NewCertificate(Guid accountId, Guid siteId, string domain, DateTimeOffset now)
    {
        return new Certificate(
            Guid.NewGuid(), accountId, siteId, domain, CertificateSource.Acme, now.AddDays(90), now);
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    /// <returns>An authenticated client.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Issues one request, giving each POST route a body its validator accepts.</summary>
    /// <remarks>
    /// The bodies must be VALID. A route whose body fails validation answers 400, which would make an
    /// authorization theory pass without the request ever reaching the handler whose tenant scoping is
    /// the thing under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    /// <returns>The response.</returns>
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
    /// <returns>The body.</returns>
    private static JsonContent BodyFor(string path)
    {
        if (path.EndsWith("/custom", StringComparison.Ordinal))
        {
            return JsonContent.Create(new
            {
                domain = "new.example.com",
                certificatePem = "-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----",
                privateKeyPem = "-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----",
            });
        }

        return JsonContent.Create(new { domain = "new.example.com" });
    }

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="OwnCertificateId">The certificate belonging to the signed-in customer.</param>
    /// <param name="StrangerCertificateId">The certificate belonging to the other tenant.</param>
    private sealed record SeededWorld(Guid OwnCertificateId, Guid StrangerCertificateId);
}
