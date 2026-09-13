using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Firewall.Controllers;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Who may reach the firewall surface over real HTTP: an anonymous caller, and a signed-in customer
/// who is not an administrator.
/// </summary>
/// <remarks>
/// <para>
/// The gate is a ROLE, not ownership, so the questions are not the tenant ones the site and database
/// fixtures ask. There is no tenant dimension here at all: a firewall rule, a ban and a whitelist row
/// are facts about the whole machine. An anonymous caller is answered 401 and a signed-in customer
/// 403 — the same answers <c>AccountsController</c> gives, which is the admin-gating idiom this
/// controller mirrors. 404 would be the wrong answer: it is the tenant answer, and using it here
/// would say a rule "does not exist" to a caller who is simply not an administrator.
/// </para>
/// <para>
/// This class holds the gating question and nothing else. What the surface DOES once a caller is
/// past the gate — what a mutation sends the agent, what it stores, how a failure is reported and
/// what it journals — is <see cref="FirewallEndpointTests"/>. The two split because they change for
/// different reasons: a new route changes the table below and the coverage assertion that reads it,
/// while a change to what a call sends the agent changes neither.
/// </para>
/// <para>
/// The agent is the only substitution, and only because it cannot be present: it is a separate root
/// process that rewrites an nftables ruleset.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class FirewallAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route the firewall surface declares, across its three controllers.</summary>
    /// <remarks>
    /// Completeness is asserted by <see cref="Every_firewall_route_is_covered_by_the_gating_fixture"/>
    /// rather than trusted: a new route added without a row here would otherwise enjoy no proof that
    /// it is closed to a customer at all.
    /// </remarks>
    public static TheoryData<string, string> FirewallEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/firewall/rules" },
            { "POST", "/api/v1/firewall/rules" },
            { "DELETE", "/api/v1/firewall/rules" },
            { "GET", "/api/v1/firewall/bans" },
            { "POST", "/api/v1/firewall/bans" },
            { "DELETE", "/api/v1/firewall/bans" },
            { "GET", "/api/v1/firewall/whitelist" },
            { "POST", "/api/v1/firewall/whitelist" },
            { "DELETE", "/api/v1/firewall/whitelist/{id}" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public FirewallAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every firewall endpoint.</summary>
    [Theory]
    [MemberData(nameof(FirewallEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_firewall_endpoint(string method, string path)
    {
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Substitute(path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed in customer is refused by every firewall endpoint and told so plainly.</summary>
    [Theory]
    [MemberData(nameof(FirewallEndpoints))]
    public async Task A_signed_in_customer_is_refused_by_every_firewall_endpoint_and_told_so_plainly(
        string method,
        string path)
    {
        // 403 and deliberately not 404. The tenant rule — another customer's row answers "not
        // found" — exists so that an identifier cannot be used as an oracle. There is no tenant
        // here and no identifier to probe: the whole surface is the server's, and a customer is
        // simply not an administrator.
        await using var factory = CreateFactory(new StubAgentFirewallClient());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Every firewall route is covered by the gating fixture.</summary>
    [Fact]
    public void Every_firewall_route_is_covered_by_the_gating_fixture()
    {
        // A hand-written list of routes goes stale the first time somebody adds one. These are read
        // off the controllers themselves, so a new endpoint fails HERE — naming itself — rather than
        // quietly enjoying no proof that it is closed to a customer.
        var declared = ControllerRoutes.Declared<FirewallRulesController>()
            .Concat(ControllerRoutes.Declared<FirewallBansController>())
            .Concat(ControllerRoutes.Declared<FirewallWhitelistController>())
            .ToList();
        Assert.NotEmpty(declared);

        var inFixture = FirewallEndpoints()
            .Select(row =>
            {
                return $"{row[0]} {row[1]}";
            })
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(route =>
        {
            return !inFixture.Contains(route);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These firewall routes are absent from FirewallEndpoints(), so nothing proves they are "
            + "closed to a signed-in customer: " + string.Join(", ", missing));
    }

    /// <summary>Substitutes a row identifier into a route template.</summary>
    /// <param name="path">The route template.</param>
    private static string Substitute(string path)
    {
        return path.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal);
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <param name="agent">The agent double every firewall call reaches.</param>
    private WebApplicationFactory<Program> CreateFactory(StubAgentFirewallClient agent)
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
                services.AddSingleton<IAgentFirewallClient>(agent);
            });
        });
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<FirewallDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one administrator and one customer.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedUsersAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        identity.Users.Add(new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now));

        await identity.SaveChangesAsync();
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

    /// <summary>Issues one request, giving each POST route a body its model binder accepts.</summary>
    /// <remarks>
    /// The bodies must be VALID. A route whose body fails validation answers 400, which would make a
    /// gating theory pass without the request ever reaching the authorization policy under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), QueryFor(method, path));
        if (method == "POST")
        {
            request.Content = BodyFor(path);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Adds the query a DELETE route binds its parameters from.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    private static string QueryFor(string method, string path)
    {
        if (method != "DELETE")
        {
            return path;
        }

        if (path.EndsWith("/rules", StringComparison.Ordinal))
        {
            return path + "?port=8080&protocol=1&sourceCidr=0.0.0.0%2F0";
        }

        return path.EndsWith("/bans", StringComparison.Ordinal) ? path + "?address=203.0.113.7" : path;
    }

    /// <summary>Builds a valid request body for one POST route.</summary>
    /// <param name="path">The absolute path being posted to.</param>
    private static JsonContent BodyFor(string path)
    {
        if (path.EndsWith("/rules", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { port = 8080, protocol = 1, sourceCidr = "0.0.0.0/0" });
        }

        if (path.EndsWith("/bans", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { address = "203.0.113.7", durationMinutes = 60 });
        }

        return JsonContent.Create(new { cidr = "203.0.113.7/32", note = "office" });
    }
}
