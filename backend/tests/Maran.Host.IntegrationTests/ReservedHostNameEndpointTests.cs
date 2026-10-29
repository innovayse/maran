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
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A well-formed host name that no tenant may claim (an address literal, the loopback family, a
/// special-use suffix, the panel's own name) is refused 400 with its own message key at every door
/// that writes a name into an nginx <c>server_name</c> or a certificate request, over real HTTP,
/// and never reaches the agent (issue #96).
/// </summary>
/// <remarks>
/// <para>
/// Definition of Done: item 2 (the real surface) and item 5 (the refusal is translated, asserted in
/// all three languages). Item 3 does not apply: no endpoint is added or changed, and the 404 for
/// another tenant's account or domain on these routes stays asserted by
/// <see cref="SitesAuthorizationTests"/> and <see cref="CertificatesAuthorizationTests"/>. Item 4 does
/// not apply: the refusal is a validator's, raised before any handler runs, and no existing validation
/// refusal is journalled.
/// </para>
/// <para>
/// The agent is replaced by <see cref="BarrierSitesAgent"/> with one expected caller (transparent), so
/// its creation counter proves a refused name never reached it and the positive controls prove a
/// legitimate name still does.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ReservedHostNameEndpointTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The key the host's encryption and token signing are configured with.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The panel name the tests that configure one use.</summary>
    private const string PanelName = "panel.example.com";

    /// <summary>The Russian resx text of the site-name refusal.</summary>
    private const string SiteDomainReservedRussian = "Это имя нельзя использовать для сайта.";

    /// <summary>The Armenian resx text of the site-name refusal.</summary>
    private const string SiteDomainReservedArmenian = "Այս անունը հնարավոր չէ օգտագործել կայքի համար։";

    /// <summary>The neutral resx text of the site-name refusal.</summary>
    private const string SiteDomainReservedEnglish = "This name cannot be used for a site.";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ReservedHostNameEndpointTests(PostgresFixture postgres)
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

    /// <summary>Names of the static reserved set, one per rule, that must be refused as a site's domain.</summary>
    public static TheoryData<string> StaticReservedNames()
    {
        return new TheoryData<string>
        {
            "203.0.113.7",
            "127.0.0.1",
            "1.2.3.4.5",
            "shop.123",
            "localhost.localhost",
            "foo.localhost",
            "x.local",
            "x.localdomain",
            "x.internal",
            "x.lan",
            "FOO.LOCAL",
        };
    }

    /// <summary>A reserved primary domain is answered 400 with the reserved key and never reaches the agent.</summary>
    /// <param name="domain">The reserved name.</param>
    [Theory]
    [MemberData(nameof(StaticReservedNames))]
    public async Task Posting_a_site_with_a_reserved_domain_is_answered_400_and_never_reaches_the_agent(string domain)
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostSiteAsync(client, world.AccountId, domain, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SiteDomainReserved", await CodeOfAsync(response));
        Assert.Equal(0, agent.CreateCalls);
    }

    /// <summary>A reserved alias is answered 400 with the alias key and never reaches the agent.</summary>
    /// <param name="alias">The reserved name.</param>
    [Theory]
    [MemberData(nameof(StaticReservedNames))]
    public async Task Posting_a_site_with_a_reserved_alias_is_answered_400_and_never_reaches_the_agent(string alias)
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostSiteAsync(client, world.AccountId, "shop.example.com", [alias]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SiteAliasReserved", await CodeOfAsync(response));
        Assert.Equal(0, agent.CreateCalls);
    }

    /// <summary>An ordinary domain with an ordinary alias still provisions: the inverse control of the refusals.</summary>
    [Fact]
    public async Task Posting_an_ordinary_site_is_still_created()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostSiteAsync(client, world.AccountId, "shop.example.com", ["www.shop.example.com"]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, agent.CreateCalls);
    }

    /// <summary>
    /// A name that merely contains a reserved word, or ends in a label with a letter, is not reserved:
    /// the refusal is categorical and not a substring match.
    /// </summary>
    [Fact]
    public async Task Posting_a_site_whose_name_only_contains_a_reserved_word_is_still_created()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostSiteAsync(
            client, world.AccountId, "localhost.example.com", ["internal.example.com", "web2.example.com"]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, agent.CreateCalls);
    }

    /// <summary>The configured panel name and its www form are refused as a site's domain, never reaching the agent.</summary>
    /// <param name="domain">The panel's own name in one of its spellings.</param>
    [Theory]
    [InlineData(PanelName)]
    [InlineData("www." + PanelName)]
    [InlineData("PANEL.Example.COM")]
    public async Task Posting_a_site_named_after_the_panel_is_answered_400_and_never_reaches_the_agent(string domain)
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent, ("Panel:HostName", PanelName));
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostSiteAsync(client, world.AccountId, domain, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SiteDomainReserved", await CodeOfAsync(response));
        Assert.Equal(0, agent.CreateCalls);
    }

    /// <summary>The panel's name as an alias is refused with the alias key.</summary>
    [Fact]
    public async Task Posting_a_site_with_the_panel_name_as_an_alias_is_answered_400_and_never_reaches_the_agent()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent, ("Panel:HostName", PanelName));
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostSiteAsync(client, world.AccountId, "shop.example.com", [PanelName]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SiteAliasReserved", await CodeOfAsync(response));
        Assert.Equal(0, agent.CreateCalls);
    }

    /// <summary>A name under the panel's domain is not the panel's own and is created: only the exact name and www are reserved.</summary>
    [Fact]
    public async Task Posting_a_site_under_the_panels_domain_is_still_created()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent, ("Panel:HostName", PanelName));
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostSiteAsync(client, world.AccountId, "shop." + PanelName, []);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, agent.CreateCalls);
    }

    /// <summary>With no panel name configured only the static set applies: the panel-shaped name is created (permissive failure direction).</summary>
    [Fact]
    public async Task Without_a_configured_panel_name_a_panel_shaped_name_is_created_but_the_static_set_still_refuses()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var created = await PostSiteAsync(client, world.AccountId, PanelName, []);
        var refused = await PostSiteAsync(client, world.AccountId, "203.0.113.7", []);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(1, agent.CreateCalls);
    }

    /// <summary>The refusal never echoes the refused name or the panel's configured name.</summary>
    [Fact]
    public async Task The_refusal_names_neither_the_refused_name_nor_the_panel()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent, ("Panel:HostName", PanelName));
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostSiteAsync(client, world.AccountId, PanelName, []);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(PanelName, body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The reserved-name refusal is written in the language the panel asked for.</summary>
    /// <param name="language">The <c>Accept-Language</c> the panel sends.</param>
    /// <param name="expected">That language's own resx value.</param>
    [Theory]
    [InlineData("en", SiteDomainReservedEnglish)]
    [InlineData("ru", SiteDomainReservedRussian)]
    [InlineData("hy", SiteDomainReservedArmenian)]
    public async Task The_reserved_name_refusal_is_written_in_the_language_the_panel_asked_for(
        string language, string expected)
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        var response = await PostSiteAsync(client, world.AccountId, "203.0.113.7", []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>A reserved name is refused as a certificate domain, ahead of the site lookup and the agent.</summary>
    /// <param name="domain">A reserved name.</param>
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("foo.localhost")]
    [InlineData("x.internal")]
    public async Task Issuing_a_certificate_for_a_reserved_name_is_answered_400(string domain)
    {
        await using var factory = CreateFactory(new BarrierSitesAgent(expectedCallers: 1));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.PostAsJsonAsync("/api/v1/certificates", new { domain });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CertificateDomainReserved", await CodeOfAsync(response));
    }

    /// <summary>The panel's own name is refused as a certificate domain.</summary>
    [Fact]
    public async Task Issuing_a_certificate_for_the_panel_name_is_answered_400()
    {
        await using var factory = CreateFactory(new BarrierSitesAgent(expectedCallers: 1), ("Panel:HostName", PanelName));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.PostAsJsonAsync("/api/v1/certificates", new { domain = "www." + PanelName });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CertificateDomainReserved", await CodeOfAsync(response));
    }

    /// <summary>
    /// An ordinary domain is not refused as reserved; it passes the validator and is answered by the
    /// tenant check instead (404 for a domain the caller does not own), which is the inverse control.
    /// </summary>
    [Fact]
    public async Task Issuing_a_certificate_for_an_ordinary_domain_is_not_refused_as_reserved()
    {
        await using var factory = CreateFactory(new BarrierSitesAgent(expectedCallers: 1));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.PostAsJsonAsync("/api/v1/certificates", new { domain = "stranger.example.com" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A reserved name is refused as the domain of custom certificate material, the second certificate door.</summary>
    /// <param name="domain">A reserved name.</param>
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("x.local")]
    public async Task Installing_custom_material_for_a_reserved_name_is_answered_400(string domain)
    {
        await using var factory = CreateFactory(new BarrierSitesAgent(expectedCallers: 1));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostCustomAsync(client, domain);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CertificateDomainReserved", await CodeOfAsync(response));
    }

    /// <summary>The panel's own name is refused as the domain of custom certificate material.</summary>
    [Fact]
    public async Task Installing_custom_material_for_the_panel_name_is_answered_400()
    {
        await using var factory = CreateFactory(new BarrierSitesAgent(expectedCallers: 1), ("Panel:HostName", PanelName));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostCustomAsync(client, PanelName);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CertificateDomainReserved", await CodeOfAsync(response));
    }

    /// <summary>An ordinary domain passes the custom-material validator and meets the tenant check (404).</summary>
    [Fact]
    public async Task Installing_custom_material_for_an_ordinary_domain_is_not_refused_as_reserved()
    {
        await using var factory = CreateFactory(new BarrierSitesAgent(expectedCallers: 1));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostCustomAsync(client, "stranger.example.com");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A reserved name is refused as the FTPS server's host name.</summary>
    /// <param name="hostname">A reserved name.</param>
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("ftp.localhost")]
    [InlineData("ftp.lan")]
    public async Task Enabling_ftps_with_a_reserved_hostname_is_answered_400(string hostname)
    {
        await using var factory = CreateFactory(new BarrierSitesAgent(expectedCallers: 1));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/ftps-server/enable", new { Hostname = hostname, PassiveAddress = string.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("FtpsHostnameReserved", await CodeOfAsync(response));
    }

    /// <summary>
    /// The FTPS door applies the static set only: the panel's own name is NOT refused as reserved there,
    /// because an operator may legitimately serve FTPS on the panel's host (threat note, "Decisions taken").
    /// </summary>
    [Fact]
    public async Task Enabling_ftps_on_the_panel_name_is_not_refused_as_reserved()
    {
        await using var factory = CreateFactory(new BarrierSitesAgent(expectedCallers: 1), ("Panel:HostName", PanelName));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/ftps-server/enable", new { Hostname = PanelName, PassiveAddress = string.Empty });

        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("FtpsHostnameReserved", content, StringComparison.Ordinal);
    }

    /// <summary>Posts one site creation.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <param name="accountId">The account the site is for.</param>
    /// <param name="domain">The primary domain.</param>
    /// <param name="aliases">The aliases.</param>
    private static Task<HttpResponseMessage> PostSiteAsync(
        HttpClient client,
        Guid accountId,
        string domain,
        string[] aliases)
    {
        return client.PostAsJsonAsync("/api/v1/sites", new
        {
            accountId,
            domain,
            aliases,
            backendType = 1,
            phpVersion = string.Empty,
            proxyUpstream = string.Empty,
        });
    }

    /// <summary>Posts custom certificate material for a domain.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <param name="domain">The domain the material is for.</param>
    private static Task<HttpResponseMessage> PostCustomAsync(HttpClient client, string domain)
    {
        return client.PostAsJsonAsync("/api/v1/certificates/custom", new
        {
            domain,
            certificatePem = "-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----",
            privateKeyPem = "-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----",
        });
    }

    /// <summary>Reads the machine-stable <c>code</c> member out of a problem document.</summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The error code, which is the resource key.</returns>
    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the sites agent replaced.</summary>
    /// <param name="agent">The agent double every site call reaches.</param>
    /// <param name="extraSettings">Further configuration, such as the panel's host name.</param>
    private WebApplicationFactory<Program> CreateFactory(
        BarrierSitesAgent agent,
        params (string Key, string Value)[] extraSettings)
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

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            foreach (var (settingKey, settingValue) in extraSettings)
            {
                builder.UseSetting(settingKey, settingValue);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentSitesClient>(agent);
            });
        });
    }

    /// <summary>Applies every module's migrations these tests need, the way the installer does.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SslDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one administrator and one customer owning an account on a roomy plan.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The customer's account identifier.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var account = new Account(Guid.NewGuid(), "customer", "customer.example.com", planId, now);
        accounts.Accounts.Add(account);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(account.Id);
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        return new SeededWorld(account.Id);
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="AccountId">The customer's account.</param>
    private sealed record SeededWorld(Guid AccountId);
}
