using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Seeders;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// DNSSEC over real HTTP against real PostgreSQL: a zone is switched on, withdrawn and refused its ending until
/// the wait has passed; the answer carries the DS record and never the private key; the key is encrypted in the
/// column the database holds; the owner and an administrator may switch it on; and each refusal has an accepting
/// control. The wait itself is exercised with an injected clock in the module's tests, because a token issued
/// 48 hours ahead of the system clock would not authenticate here.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class DnsZoneDnssecEndpointTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The key the host is configured with for encryption and token signing.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The shared PostgreSQL server this test's database lives on.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DnsZoneDnssecEndpointTests(PostgresFixture postgres)
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

    /// <summary>
    /// An administrator walks a zone through switching on and starting a withdrawal, the ending is refused as too
    /// early, and every step is journalled without a key.
    /// </summary>
    [Fact]
    public async Task An_administrator_signs_a_zone_and_the_early_ending_of_the_withdrawal_is_refused()
    {
        await using var factory = CreateFactory();
        var world = await SeedAsync(factory);
        using var admin = await SignInAsync(factory, "admin");
        var zoneId = await CreateZoneAsync(admin, world.OwnAccountId);

        var before = await admin.GetAsync($"/api/v1/dns-zones/{zoneId}/dnssec");
        var enabled = await admin.PostAsync($"/api/v1/dns-zones/{zoneId}/dnssec/enable", null);
        var again = await admin.PostAsync($"/api/v1/dns-zones/{zoneId}/dnssec/enable", null);
        var withdrawn = await admin.PostAsync($"/api/v1/dns-zones/{zoneId}/dnssec/withdraw", null);
        var early = await admin.PostAsJsonAsync($"/api/v1/dns-zones/{zoneId}/dnssec/finish-withdrawal", new { dsRecordRemoved = true });

        using var offBody = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
        Assert.Equal("off", offBody.RootElement.GetProperty("state").GetString());
        Assert.True(offBody.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, offBody.RootElement.GetProperty("ds").ValueKind);

        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var enabledText = await enabled.Content.ReadAsStringAsync();
        using var enabledBody = JsonDocument.Parse(enabledText);
        Assert.Equal("signing", enabledBody.RootElement.GetProperty("state").GetString());
        Assert.Equal(13, enabledBody.RootElement.GetProperty("ds").GetProperty("algorithm").GetInt32());
        Assert.Equal(2, enabledBody.RootElement.GetProperty("ds").GetProperty("digestType").GetInt32());
        Assert.Equal(64, enabledBody.RootElement.GetProperty("ds").GetProperty("digest").GetString()!.Length);
        Assert.False(enabledBody.RootElement.GetProperty("rolloverAutomatic").GetBoolean());
        Assert.DoesNotContain("private", enabledText, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("ZoneSigningNotOff", await CodeOfAsync(again));
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        using var withdrawnBody = JsonDocument.Parse(await withdrawn.Content.ReadAsStringAsync());
        Assert.Equal("withdrawing", withdrawnBody.RootElement.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, withdrawnBody.RootElement.GetProperty("withdrawalEarliestFinishAt").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("ZoneSigningWithdrawalTooEarly", await CodeOfAsync(early));

        var actions = await ActionsAsync(factory);
        Assert.Contains((DnsAuditActions.ZoneSigningEnabled, true), actions);
        Assert.Contains((DnsAuditActions.ZoneSigningEnabled, false), actions);
        Assert.Contains((DnsAuditActions.ZoneSigningWithdrawalStarted, true), actions);
        Assert.Contains((DnsAuditActions.ZoneSigningWithdrawn, false), actions);
    }

    /// <summary>The ending of a withdrawal without the statement that the DS record was removed is a 400 with its code.</summary>
    [Theory]
    [InlineData("{\"dsRecordRemoved\":false}")]
    [InlineData("{}")]
    public async Task Ending_a_withdrawal_without_the_statement_is_refused_with_its_code(string body)
    {
        await using var factory = CreateFactory();
        var world = await SeedAsync(factory);
        using var admin = await SignInAsync(factory, "admin");
        var zoneId = await CreateZoneAsync(admin, world.OwnAccountId);
        await admin.PostAsync($"/api/v1/dns-zones/{zoneId}/dnssec/enable", null);
        await admin.PostAsync($"/api/v1/dns-zones/{zoneId}/dnssec/withdraw", null);

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var refused = await admin.PostAsync($"/api/v1/dns-zones/{zoneId}/dnssec/finish-withdrawal", content);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("ZoneSigningDsRemovalNotConfirmed", await CodeOfAsync(refused));
    }

    /// <summary>The zone's owner may switch signing on, and sees the DS record and the state on the zone read.</summary>
    [Fact]
    public async Task The_owner_may_sign_their_own_zone()
    {
        await using var factory = CreateFactory();
        var world = await SeedAsync(factory);
        using var admin = await SignInAsync(factory, "admin");
        var zoneId = await CreateZoneAsync(admin, world.OwnAccountId);
        using var customer = await SignInAsync(factory, "customer");

        var enabled = await customer.PostAsync($"/api/v1/dns-zones/{zoneId}/dnssec/enable", null);
        var zone = await customer.GetAsync($"/api/v1/dns-zones/{zoneId}");

        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        using var zoneBody = JsonDocument.Parse(await zone.Content.ReadAsStringAsync());
        Assert.Equal("signing", zoneBody.RootElement.GetProperty("dnssecState").GetString());
    }

    /// <summary>
    /// The private key is encrypted in the column the database holds: the raw value is not the 44-character scalar
    /// the panel reads back through its own context, and the panel reads back a scalar of exactly 32 bytes.
    /// </summary>
    [Fact]
    public async Task The_private_key_is_encrypted_in_the_column_the_database_holds()
    {
        await using var factory = CreateFactory();
        var world = await SeedAsync(factory);
        using var admin = await SignInAsync(factory, "admin");
        var zoneId = await CreateZoneAsync(admin, world.OwnAccountId);
        await admin.PostAsync($"/api/v1/dns-zones/{zoneId}/dnssec/enable", null);

        using var scope = factory.Services.CreateScope();
        var dns = scope.ServiceProvider.GetRequiredService<DnsDbContext>();
        var connection = dns.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select \"PrivateKey\" from dns.\"ZoneSigningKeys\"";
        var raw = (string)(await command.ExecuteScalarAsync())!;
        var plain = (await dns.ZoneSigningKeys.IgnoreQueryFilters().AsNoTracking().SingleAsync()).PrivateKey;

        Assert.Equal(32, Convert.FromBase64String(plain).Length);
        Assert.NotEqual(plain, raw);
        Assert.DoesNotContain(plain, raw, StringComparison.Ordinal);
        Assert.True(raw.Length > plain.Length);
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

    /// <summary>Creates an empty zone for an account as the administrator.</summary>
    /// <param name="admin">The administrator's client.</param>
    /// <param name="accountId">The owning account.</param>
    /// <returns>The zone's identity.</returns>
    private static async Task<Guid> CreateZoneAsync(HttpClient admin, Guid accountId)
    {
        var created = await admin.PostAsJsonAsync("/api/v1/dns-zones", new { accountId, name = "signed.example" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Reads the machine <c>code</c> of a problem response.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The code.</returns>
    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("code").GetString();
    }

    /// <summary>Reads the audit journal's action names and outcomes.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The pairs, in the order they were written.</returns>
    private static async Task<List<(string Action, bool Succeeded)>> ActionsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
            .AsNoTracking()
            .OrderBy(entry => entry.OccurredAt)
            .Select(entry => new { entry.Action, entry.Succeeded })
            .ToListAsync();

        return rows.Select(row =>
        {
            return (row.Action, row.Succeeded);
        }).ToList();
    }

    /// <summary>
    /// Migrates every schema the tests touch, seeds an account, an administrator and a customer of the account, and a
    /// complete, switched-on set of name server settings.
    /// </summary>
    /// <param name="factory">The host.</param>
    /// <returns>The identifiers the tests aim at.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var dns = scope.ServiceProvider.GetRequiredService<DnsDbContext>();
        await identity.Database.MigrateAsync();
        await accounts.Database.MigrateAsync();
        await dns.Database.MigrateAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5, 3));
        var own = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        accounts.Accounts.Add(own);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        identity.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), customer.Id, own.Id, now));
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<ZoneTemplateSeeder>().SeedAsync(CancellationToken.None);
        var settings = await dns.DnsSettings.SingleAsync();
        settings.Save("ns1.panel.test", "ns2.panel.test", "hostmaster.panel.test", "203.0.113.5", string.Empty, 3600, now);
        settings.Enable(now);
        await dns.SaveChangesAsync();

        return new SeededWorld(own.Id);
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

    /// <summary>The identities the tests aim at.</summary>
    /// <param name="OwnAccountId">The account the customer belongs to.</param>
    private sealed record SeededWorld(Guid OwnAccountId);
}
