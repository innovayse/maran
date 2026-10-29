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
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Two customers posting sites that name one host in the same instant, over real HTTP against real
/// PostgreSQL: one is answered 201, the other 409 <c>SiteDomainTaken</c>, and neither is ever a 500.
/// </summary>
/// <remarks>
/// <para>
/// The agent is replaced by <see cref="BarrierSitesAgent"/>, which holds both creations inside the
/// agent call until both have arrived (the same pattern the firewall and monitoring endpoint tests use,
/// and like them it replaces the resilient decorator the host would otherwise put in front of the
/// client). Without the barrier the two requests would be serialised by chance and could not lose.
/// </para>
/// <para>
/// Definition of Done: item 2 (the real surface) and item 4 (the refusal is journalled, read back
/// through the administrator's audit endpoint). Item 3 does not apply: no endpoint is added, and the
/// 404 for another tenant's account on this route is asserted by <see cref="SitesAuthorizationTests"/>.
/// A 409 for a name another tenant holds is deliberate, since hostnames are unique server-wide.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SiteHostnameRaceEndpointTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The key the host's encryption and token signing are configured with.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteHostnameRaceEndpointTests(PostgresFixture postgres)
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

    /// <summary>Two requests racing for one hostname answer 201 and 409 and never 500.</summary>
    [Fact]
    public async Task Two_requests_racing_for_one_hostname_answer_201_and_409_and_never_500()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var first = await SignInAsync(factory, "first");
        using var second = await SignInAsync(factory, "second");

        var responses = await Task.WhenAll(
            PostSiteAsync(first, world.FirstAccountId, "shop.example.com", []),
            PostSiteAsync(second, world.SecondAccountId, "zero.example.com", ["shop.example.com"]));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(response => { return response.StatusCode; }).Order().ToList());

        var refused = responses.Single(response => { return response.StatusCode == HttpStatusCode.Conflict; });
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("SiteDomainTaken", problem.RootElement.GetProperty("code").GetString());
        var title = problem.RootElement.GetProperty("title").GetString();
        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.NotEqual("SiteDomainTaken", title);
        Assert.Single(agent.DeletedDomains);
    }

    /// <summary>A request the agent refuses as already served answers 409 domain taken and deletes nothing.</summary>
    [Fact]
    public async Task A_request_the_agent_refuses_as_already_served_answers_409_domain_taken()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1)
        {
            RefuseCreationWith = Error.Of("AgentAlreadyExists", ErrorType.Conflict),
        };
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "first");

        var response = await PostSiteAsync(client, world.FirstAccountId, "shop.example.com", []);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SiteDomainTaken", problem.RootElement.GetProperty("code").GetString());
        Assert.Empty(agent.DeletedDomains);
    }

    /// <summary>The refused request is journalled as a failed site creation, beside the winner's success.</summary>
    [Fact]
    public async Task The_refused_request_is_journalled_as_a_failed_site_creation()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var first = await SignInAsync(factory, "first");
        using var second = await SignInAsync(factory, "second");
        using var admin = await SignInAsync(factory, "admin");

        var responses = await Task.WhenAll(
            PostSiteAsync(first, world.FirstAccountId, "shop.example.com", []),
            PostSiteAsync(second, world.SecondAccountId, "zero.example.com", ["shop.example.com"]));
        var loserDomain = responses[0].StatusCode == HttpStatusCode.Created ? "zero.example.com" : "shop.example.com";

        var audit = await admin.GetAsync("/api/v1/audit?limit=500");
        using var body = JsonDocument.Parse(await audit.Content.ReadAsStringAsync());
        var entries = body.RootElement.EnumerateArray()
            .Select(entry =>
            {
                return (
                    entry.GetProperty("action").GetString(),
                    entry.GetProperty("subject").GetString(),
                    entry.GetProperty("succeeded").GetBoolean());
            })
            .ToList();

        Assert.Contains(("SiteCreated", loserDomain, false), entries);
        Assert.Equal(1, entries.Count(entry => { return entry is { Item1: "SiteCreated", Item3: true }; }));
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

    /// <summary>Boots the host against this class's PostgreSQL, with the sites agent replaced.</summary>
    /// <param name="agent">The agent double every site call reaches.</param>
    private WebApplicationFactory<Program> CreateFactory(BarrierSitesAgent agent)
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
    }

    /// <summary>Seeds one administrator and two customers, each owning an account on a roomy plan.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The two accounts' identifiers.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var firstAccount = new Account(Guid.NewGuid(), "first", "first.example.com", planId, now);
        var secondAccount = new Account(Guid.NewGuid(), "second", "second.example.com", planId, now);
        accounts.Accounts.AddRange(firstAccount, secondAccount);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var first = new User(
            Guid.NewGuid(), "first", "first@example.com", hasher.Hash(Password), UserRole.Customer, now);
        first.AssignAccount(firstAccount.Id);
        var second = new User(
            Guid.NewGuid(), "second", "second@example.com", hasher.Hash(Password), UserRole.Customer, now);
        second.AssignAccount(secondAccount.Id);
        identity.Users.AddRange(first, second);
        await identity.SaveChangesAsync();

        return new SeededWorld(firstAccount.Id, secondAccount.Id);
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
    /// <param name="FirstAccountId">The first customer's account.</param>
    /// <param name="SecondAccountId">The second customer's account.</param>
    private sealed record SeededWorld(Guid FirstAccountId, Guid SecondAccountId);
}
