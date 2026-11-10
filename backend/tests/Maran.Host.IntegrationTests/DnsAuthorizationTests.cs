using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Persistence;
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
/// The DNS surface over real HTTP against real PostgreSQL: the IDOR fixture every tenant endpoint is
/// required to have (rules/testing.md, Definition of Done 3), the role gate the server endpoints carry
/// instead, and the inverse controls that keep both honest. Another account's zone answers 404 on every
/// route that names one, a customer is refused 403 on the administrator's routes, an anonymous caller 401.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class DnsAuthorizationTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The key the host is configured with for encryption and token signing.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The routes that name one of another account's zones or records, with the method each takes.</summary>
    /// <returns>The method and path template rows.</returns>
    public static TheoryData<string, string> ZoneScopedEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/dns-zones/{id}" },
            { "DELETE", "/api/v1/dns-zones/{id}" },
            { "GET", "/api/v1/dns-zones/{id}/records" },
            { "POST", "/api/v1/dns-zones/{id}/records" },
            { "PUT", "/api/v1/dns-zones/{id}/records" },
            { "PUT", "/api/v1/dns-zones/{id}/records/00000000-0000-4000-8000-000000000001" },
            { "DELETE", "/api/v1/dns-zones/{id}/records/00000000-0000-4000-8000-000000000001" },
            { "GET", "/api/v1/dns-zones/{id}/dnssec" },
            { "POST", "/api/v1/dns-zones/{id}/dnssec/enable" },
            { "POST", "/api/v1/dns-zones/{id}/dnssec/withdraw" },
            { "POST", "/api/v1/dns-zones/{id}/dnssec/finish-withdrawal" },
        };
    }

    /// <summary>The administrator's routes, which a customer must be refused.</summary>
    /// <returns>The method and path rows.</returns>
    public static TheoryData<string, string> AdministratorEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/dns-server" },
            { "POST", "/api/v1/dns-server/enable" },
            { "POST", "/api/v1/dns-server/disable" },
            { "GET", "/api/v1/dns-settings" },
            { "PUT", "/api/v1/dns-settings" },
        };
    }

    /// <summary>The shared PostgreSQL server this test's database lives on.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DnsAuthorizationTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Creates this test's database.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Nothing to release: the database is dropped with the server.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>An anonymous caller is refused by every zone route.</summary>
    [Theory]
    [MemberData(nameof(ZoneScopedEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_zone_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer reaching for another account's zone is answered 404, never 403.</summary>
    [Theory]
    [MemberData(nameof(ZoneScopedEndpoints))]
    public async Task A_customer_reaching_for_another_accounts_zone_is_answered_not_found(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, path.Replace("{id}", world.StrangerZoneId.ToString(), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A customer is refused 403 on every administrator route.</summary>
    [Theory]
    [MemberData(nameof(AdministratorEndpoints))]
    public async Task A_customer_is_refused_the_administrators_dns_routes(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Another account's zone survives a delete aimed at it, which is the property the 404 stands for.</summary>
    [Fact]
    public async Task A_customer_deleting_another_accounts_zone_leaves_it_in_place()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.DeleteAsync($"/api/v1/dns-zones/{world.StrangerZoneId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dns = scope.ServiceProvider.GetRequiredService<DnsDbContext>();
        var survivors = await dns.Zones.IgnoreQueryFilters().Where(row => row.Id == world.StrangerZoneId).CountAsync();
        Assert.Equal(1, survivors);
    }

    /// <summary>Inverse control: the owner reads, lists and deletes their own zone.</summary>
    [Fact]
    public async Task A_customer_reads_lists_and_deletes_their_own_zone()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var read = await client.GetAsync($"/api/v1/dns-zones/{world.OwnZoneId}");
        var list = await client.GetAsync("/api/v1/dns-zones");
        var records = await client.GetAsync($"/api/v1/dns-zones/{world.OwnZoneId}/records");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var names = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(row =>
            {
                return row.GetProperty("name").GetString();
            }).ToList();
        Assert.Equal(["own.example"], names);
        using var recordBody = JsonDocument.Parse(await records.Content.ReadAsStringAsync());
        Assert.Equal("www", recordBody.RootElement.GetProperty("items")[0].GetProperty("name").GetString());

        var deleted = await client.DeleteAsync($"/api/v1/dns-zones/{world.OwnZoneId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    /// <summary>Inverse control: the administrator reads the server status and every account's zones.</summary>
    [Fact]
    public async Task An_administrator_reads_the_server_and_every_zone()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var status = await client.GetAsync("/api/v1/dns-server");
        var list = await client.GetAsync("/api/v1/dns-zones");

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>Builds the host against this test's database with the agent stubbed.</summary>
    /// <returns>The factory.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new ConfiguredHostFactory(builder =>
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
                services.AddSingleton<IAgentDnsClient>(new StubAgentDnsClient());
            });
        });
    }

    /// <summary>Applies the migrations of every schema the tests touch.</summary>
    /// <param name="factory">The host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DnsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts, an administrator, a customer of the first, and a zone each with a record on the first.</summary>
    /// <param name="factory">The host.</param>
    /// <returns>The identifiers the tests aim at.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var dns = scope.ServiceProvider.GetRequiredService<DnsDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5, 3));
        var own = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        var stranger = new Account(Guid.NewGuid(), "stranger", "stranger.example.com", planId, now);
        accounts.Accounts.AddRange(own, stranger);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        identity.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), customer.Id, own.Id, now));
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        var ownZone = new Zone(Guid.NewGuid(), own.Id, "own.example", now);
        ownZone.AddRecord(Guid.NewGuid(), new RecordDraft("www", RecordType.A, 300, "203.0.113.7"), 10, now);
        var strangerZone = new Zone(Guid.NewGuid(), stranger.Id, "stranger.example", now);
        dns.Zones.AddRange(ownZone, strangerZone);
        await dns.SaveChangesAsync();

        return new SeededWorld(ownZone.Id, strangerZone.Id);
    }

    /// <summary>Signs a seeded user in and returns a client carrying the access token.</summary>
    /// <param name="factory">The host.</param>
    /// <param name="username">The seeded user.</param>
    /// <returns>The authenticated client.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Sends one request with a body that is valid enough to reach the handler on a write.</summary>
    /// <param name="client">The client.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = BodyFor(method, path);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Builds a request body that passes validation for the route it is sent to.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    /// <returns>The JSON body.</returns>
    private static JsonContent BodyFor(string method, string path)
    {
        var record = new { name = "www", type = "a", ttl = 300, value = "203.0.113.7", priority = 0, weight = 0, port = 0, flags = 0, tag = string.Empty };
        if (path.EndsWith("/records", StringComparison.Ordinal) && method == "PUT")
        {
            return JsonContent.Create(new { records = new[] { record } });
        }

        if (path.Contains("/records", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { record });
        }

        if (path.EndsWith("/dnssec/finish-withdrawal", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { dsRecordRemoved = true });
        }

        if (path.Contains("dns-settings", StringComparison.Ordinal))
        {
            return JsonContent.Create(new
            {
                primaryNameserver = "ns1.panel.test",
                secondaryNameserver = string.Empty,
                hostmasterMailbox = "hostmaster.panel.test",
                publicIpv4 = string.Empty,
                publicIpv6 = string.Empty,
                defaultTtl = 3600,
            });
        }

        return JsonContent.Create(new { });
    }

    /// <summary>The zones the tests aim at.</summary>
    /// <param name="OwnZoneId">The zone of the customer's own account.</param>
    /// <param name="StrangerZoneId">The zone of the other account.</param>
    private sealed record SeededWorld(Guid OwnZoneId, Guid StrangerZoneId);
}
