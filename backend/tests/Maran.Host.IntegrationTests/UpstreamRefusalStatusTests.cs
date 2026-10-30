using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Options;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// An outside party's refusal and a precondition the server does not yet meet are answered with
/// their own statuses over real HTTP — 502 and 409 — and never with the 500 of a local fault
/// (issue #204).
/// </summary>
/// <remarks>
/// <para>
/// The status is derived from the error's kind alone, and the kind is decided where the error is
/// built. These tests observe both halves together: the real handler classifies, the real
/// translation answers, and only the parties a test cannot run — the certificate authority and the
/// root agent — are replaced.
/// </para>
/// <para>
/// Each test asserts the problem document's <c>code</c> as well as the status, so a different
/// failure that happened to land on the same status cannot pass for the one under test.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class UpstreamRefusalStatusTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The customer's site, the domain a certificate is requested for.</summary>
    private const string SiteDomain = "own.example.com";

    /// <summary>The site FTPS is enabled for.</summary>
    private const string FtpsHostname = "ftp.example.com";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public UpstreamRefusalStatusTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A certificate authority refusing the registration answers 502 with its code.</summary>
    [Fact]
    public async Task A_certificate_authority_refusing_the_registration_answers_502_with_its_code()
    {
        await using var factory = CreateFactory();
        await PrepareAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.PostAsJsonAsync("/api/v1/certificates", new { domain = SiteDomain });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("AcmeAccountRejected", await CodeOfAsync(response));
    }

    /// <summary>Enabling FTPS for a served hostname with no certificate answers 409 with its code.</summary>
    [Fact]
    public async Task Enabling_ftps_for_a_served_hostname_with_no_certificate_answers_409_with_its_code()
    {
        await using var factory = CreateFactory();
        await PrepareAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/ftps-server/enable", new { Hostname = FtpsHostname, PassiveAddress = string.Empty });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("FtpsCertificateMissing", await CodeOfAsync(response));
    }

    /// <summary>Reads the machine-stable <c>code</c> member out of a problem document.</summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The error code.</returns>
    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>
    /// Boots the host against this class's PostgreSQL, with the authority and the agent's FTPS half
    /// replaced.
    /// </summary>
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
            builder.UseSetting("Acme:DirectoryUrl", RefusingAcmeAuthority.DirectoryUrl);

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(AcmeOptions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => { return new RefusingAcmeAuthority(); });
                services.AddSingleton<IAgentFtpsClient>(new CertificateMissingFtpsAgent());
            });
        });
    }

    /// <summary>Migrates every module and seeds an administrator, a customer, and two sites.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task PrepareAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await ModuleSchemas.MigrateAsync(scope.ServiceProvider);

        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var account = new Account(Guid.NewGuid(), "own", SiteDomain, planId, now);
        accounts.Accounts.Add(account);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(account.Id);
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        sites.Sites.AddRange(NewSite(account.Id, SiteDomain, now), NewSite(account.Id, FtpsHostname, now));
        await sites.SaveChangesAsync();
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
            $"/home/own/sites/{domain}",
            now);
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
}
