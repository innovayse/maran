using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Seeders;
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
using Microsoft.Extensions.Options;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The sequence that used to end with a permanently empty whitelist — install, sign in, tidy up —
/// asked at the surface an operator actually meets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is here and not only beside the handler.</b> The refusal depends on a value the handler
/// never computes: the caller's address, which arrives through the forwarded-header pipeline, is
/// rendered by <c>ClientAddress</c> and is put on the command by the controller. A handler test
/// hands that string in directly and would stay green if the controller stopped passing it, if the
/// pipeline stopped believing the proxy, or if the status mapping answered something other than 409
/// — every one of which is what the operator would actually see.
/// </para>
/// <para>
/// <b>The seed is written by the real seeder</b>, not by inserting a row, so the thing under test is
/// the row the installer produces. It is invoked after the migrations rather than left to the host's
/// startup task, because a test database has no tables at the moment that task runs; the code path
/// is the same one, called at the moment it would have succeeded.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class WhitelistSelfLockoutTests : IAsyncLifetime
{
    /// <summary>The password the seeded administrator signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The key the test host uses for both encryption and JWT signing.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The range the installer seeds — the address the install was run from.</summary>
    private const string SeededCidr = "203.0.113.7/32";

    /// <summary>The administrator arriving from the seeded address, as the panel renders them.</summary>
    private const string FromInstallAddress = "203.0.113.7";

    /// <summary>The administrator arriving from somewhere the seeded range does not cover.</summary>
    private const string FromElsewhere = "198.51.100.4";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public WhitelistSelfLockoutTests(PostgresFixture postgres)
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

    /// <summary>Deleting the seeded row from the address it exempts is refused with a way out.</summary>
    /// <returns>Resolves once the refusal has been read back.</returns>
    /// <remarks>
    /// The defect, end to end: this DELETE used to answer 200, and because
    /// <c>WhitelistSeedRecord</c> blocks re-seeding, the whitelist was then empty for the life of the
    /// server. The body is asserted as well as the status because a refusal an operator cannot act
    /// on is how somebody ends up deleting the row in psql instead.
    /// </remarks>
    [Fact]
    public async Task Deleting_the_row_that_exempts_the_caller_is_refused_and_the_row_stays()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var seeded = await SeedWhitelistAsync(factory);
        using var client = await SignInAsync(factory, FromInstallAddress);

        var response = await client.DeleteAsync($"/api/v1/firewall/whitelist/{seeded}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WhitelistEntryProtectsCaller", body.RootElement.GetProperty("code").GetString());
        Assert.Equal([SeededCidr], await CidrsAsync(factory));
    }

    /// <summary>The operator's documented way out works: cover the address, then remove the row.</summary>
    /// <returns>Resolves once the second attempt has succeeded.</returns>
    /// <remarks>
    /// Half one of the inverse control. A guard that refused every removal, or one that pinned the
    /// seeded row or the last row, passes the test above and fails here — and so would a change that
    /// left the error message describing an escape the panel does not actually offer.
    /// </remarks>
    [Fact]
    public async Task Adding_a_range_that_also_covers_the_caller_lets_the_refused_row_go()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var seeded = await SeedWhitelistAsync(factory);
        using var client = await SignInAsync(factory, FromInstallAddress);

        var added = await client.PostAsJsonAsync(
            "/api/v1/firewall/whitelist", new { cidr = "203.0.113.0/24", note = "the office network" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var response = await client.DeleteAsync($"/api/v1/firewall/whitelist/{seeded}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["203.0.113.0/24"], await CidrsAsync(factory));
    }

    /// <summary>An operator elsewhere may still revoke the install address, last row and all.</summary>
    /// <returns>Resolves once the whitelist has been read back empty.</returns>
    /// <remarks>
    /// Half two of the inverse control, and the case that decided the shape of the guard. The
    /// installer's address is routinely a café network, a jump host or a shared NAT egress that must
    /// never be trusted again — <c>WhitelistSeedRecord</c>'s own reasoning — so an administrator who
    /// has moved elsewhere is exactly the person who should be able to delete it, down to the last
    /// row. A guard about the seeded row, or about the last row, would refuse this.
    /// </remarks>
    [Fact]
    public async Task An_operator_at_another_address_may_delete_the_seeded_row_and_empty_the_list()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var seeded = await SeedWhitelistAsync(factory);
        using var client = await SignInAsync(factory, FromElsewhere);

        var response = await client.DeleteAsync($"/api/v1/firewall/whitelist/{seeded}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await CidrsAsync(factory));
    }

    /// <summary>The refused removal is journalled as a failure naming the range that was kept.</summary>
    /// <returns>Resolves once the journal has been read back.</returns>
    [Fact]
    public async Task The_refused_removal_is_journalled_as_a_failure_naming_the_range()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        var seeded = await SeedWhitelistAsync(factory);
        using var client = await SignInAsync(factory, FromInstallAddress);

        await client.DeleteAsync($"/api/v1/firewall/whitelist/{seeded}");

        var response = await client.GetAsync("/api/v1/audit");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var refusals = body.RootElement.EnumerateArray()
            .Where(entry =>
            {
                return entry.GetProperty("action").GetString() == AuditActions.FirewallWhitelistChanged
                    && !entry.GetProperty("succeeded").GetBoolean();
            })
            .Select(entry =>
            {
                return entry.GetProperty("subject").GetString();
            })
            .ToList();

        Assert.Equal([SeededCidr], refusals);
    }

    /// <summary>Reads the ranges the panel still exempts.</summary>
    /// <param name="factory">The booted host, whose services reach this test's database.</param>
    /// <returns>Every stored range.</returns>
    private static async Task<List<string>> CidrsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var firewall = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        return await firewall.WhitelistEntries.AsNoTracking()
            .Select(entry => entry.Cidr)
            .ToListAsync();
    }

    /// <summary>Runs the installer's own seeder, and hands back the row it wrote.</summary>
    /// <param name="factory">The booted host, whose services hold the seeder.</param>
    /// <returns>The seeded row's identifier, as the panel's own list would show it.</returns>
    private static async Task<Guid> SeedWhitelistAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<WhitelistSeeder>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<FirewallOptions>>();

        Assert.True(await seeder.SeedAsync(options.Value, CancellationToken.None));

        var firewall = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        var entry = Assert.Single(await firewall.WhitelistEntries.AsNoTracking().ToListAsync());
        Assert.Equal(SeededCidr, entry.Cidr);
        Assert.Equal(WhitelistSeeder.SeedNote, entry.Note);

        return entry.Id;
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Resolves once both modules' tables exist.</returns>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<FirewallDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds the one administrator these tests act as.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Resolves once the row exists.</returns>
    private static async Task SeedUsersAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));

        await identity.SaveChangesAsync();
    }

    /// <summary>Signs the administrator in from one address and keeps arriving from it.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="address">The address every request from this client appears to arrive from.</param>
    /// <returns>A client carrying the access token and the forwarded address.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string address)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "admin", Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Boots a host that believes the forwarded header and carries the installer's seed.</summary>
    /// <returns>The factory; the caller disposes it.</returns>
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

            // What the installer writes into panel.env after watching the operator arrive.
            builder.UseSetting(
                $"{FirewallOptions.SectionName}:{nameof(FirewallOptions.SeedWhitelistCidr)}", SeededCidr);

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentFirewallClient>(new StubAgentFirewallClient());

                // Loopback, because that is where nginx is: it makes the panel honour the forwarded
                // header, which is the only way these tests can arrive from two different addresses.
                services.AddSingleton<IStartupFilter>(new RemotePeerStartupFilter(IPAddress.Loopback));
            });
        });
    }
}
