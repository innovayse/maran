using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Monitoring.Controllers;
using Maran.Modules.Monitoring.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The monitoring surface over real HTTP against real PostgreSQL: who may reach it, and what the
/// agent's figures look like on the wire.
/// </summary>
/// <remarks>
/// <para>
/// The gate is a ROLE, not ownership, so the questions are not the tenant ones the site and database
/// fixtures ask. There is no tenant dimension here at all: one processor, one root filesystem, one
/// set of managed services. An anonymous caller is answered 401 and
/// a signed-in customer 403 — the admin-gating idiom <c>AccountsController</c> and the firewall
/// controllers already use. 404 would be the wrong answer: it is the TENANT answer, and using it here
/// would tell a caller a resource "does not exist" when they are simply not an administrator.
/// </para>
/// <para>
/// The agent is the only substitution, and only because it cannot be present: it is a separate root
/// process reading <c>/proc</c> and the service manager.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class MonitoringAuthorizationTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Every route the monitoring surface declares.</summary>
    /// <remarks>
    /// Completeness is asserted by <see cref="Every_monitoring_route_is_covered_by_the_gating_fixture"/>
    /// rather than trusted: a new route added without a row here would otherwise enjoy no proof that
    /// it is closed to a customer at all.
    /// </remarks>
    /// <returns>The method and path of every gated route.</returns>
    public static TheoryData<string, string> MonitoringEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/monitoring/metrics" },
            { "GET", "/api/v1/monitoring/services" },
            { "GET", "/api/v1/monitoring/chart" },
            // The route this fixture matters most on: behind it is IAccountDirectory.ListAsync,
            // which applies no tenant scope at all, so the admin policy is the only thing keeping a
            // customer from every other tenant's system user name and plan allowances.
            { "GET", "/api/v1/monitoring/accounts-disk" },
        };
    }

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MonitoringAuthorizationTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>Resolves immediately; the shared server outlives the test.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>An anonymous caller is refused by every monitoring endpoint.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    [Theory]
    [MemberData(nameof(MonitoringEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_monitoring_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed in customer is refused by every monitoring endpoint and told so plainly.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    [Theory]
    [MemberData(nameof(MonitoringEndpoints))]
    public async Task A_signed_in_customer_is_refused_by_every_monitoring_endpoint_and_told_so_plainly(
        string method,
        string path)
    {
        // 403 and deliberately not 404. The tenant rule — another customer's row answers "not found"
        // — exists so an identifier cannot be used as an oracle. There is no tenant here and no
        // identifier to probe: the whole surface is the server's, and a customer is simply not an
        // administrator.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator reads the host's live figures straight from the agent.</summary>
    [Fact]
    public async Task An_administrator_reads_the_hosts_live_figures()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(7.5, body.RootElement.GetProperty("cpuPercent").GetDouble());
        Assert.Equal(2_048, body.RootElement.GetProperty("memoryUsedBytes").GetInt64());
    }

    /// <summary>A service the agent cannot judge is reported as not known, never as stopped.</summary>
    /// <remarks>
    /// <para>
    /// The three-valued state has to survive all the way to the wire. On the Debian family the enabled
    /// SSH unit is a socket whose service is inactive from boot until the first connection, so a panel
    /// that rendered "unknown" as "stopped" would show an outage on every such host at every reboot.
    /// </para>
    /// <para>
    /// The casing is asserted here too, and on the raw text rather than on a deserialized enum, because
    /// that is the only place the defect this pins was visible: the handler used to project the two
    /// names with <c>ToString()</c>, which handed out a plain string and so bypassed the panel-wide
    /// camelCase enum converter — one module answering <c>Running</c> beside its own <c>lastDay</c>.
    /// Re-introducing either <c>ToString()</c> in <c>ListServiceStatusesQueryHandler</c>, or widening
    /// <c>ServiceStatusDto</c>'s two names back to <c>string</c>, reddens this test on the exact
    /// substring a client reads.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_service_the_agent_cannot_judge_is_reported_as_not_known()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/services");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(payload);
        var rows = body.RootElement.EnumerateArray()
            .Select(row =>
            {
                return (row.GetProperty("service").GetString(), row.GetProperty("state").GetString());
            })
            .ToList();

        Assert.Contains(("webServer", "running"), rows);
        Assert.Contains(("ssh", "unknown"), rows);

        // The bytes themselves, so a reader of this test can see what a client receives. A
        // PascalCase member name anywhere in this document is the two-casings defect returning.
        Assert.Contains("\"service\":\"webServer\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"running\"", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("WebServer", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Running", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown", payload, StringComparison.Ordinal);
    }





    /// <summary>Every monitoring route is covered by the gating fixture.</summary>
    [Fact]
    public void Every_monitoring_route_is_covered_by_the_gating_fixture()
    {
        // A hand-written list of routes goes stale the first time somebody adds one. These are read
        // off the controllers themselves, so a new endpoint fails HERE — naming itself — rather than
        // quietly enjoying no proof that it is closed to a customer.
        var declared = ControllerRoutes.Declared<MonitoringController>().ToList();
        Assert.NotEmpty(declared);

        var inFixture = MonitoringEndpoints()
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
            "These monitoring routes are absent from MonitoringEndpoints(), so nothing proves they "
            + "are closed to a signed-in customer: " + string.Join(", ", missing));
    }

    /// <summary>Issues one request against a route of this surface.</summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        return await client.SendAsync(request);
    }


    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <returns>The booted host factory.</returns>
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

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentMonitorClient>(new StubAgentMonitorClient());
            });
        });
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().Database.MigrateAsync();
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
    /// <returns>A client whose requests carry the user's bearer token.</returns>
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
