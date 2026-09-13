using System.Net;
using System.Net.Http.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Whose word the running panel takes for the caller's address, asserted on the address it actually
/// writes down rather than on the options object it was configured with.
/// </summary>
/// <remarks>
/// Every per-address protection the panel has rests on this one value: the login rate limiter
/// partitions on it, the audit journal records it, the session list shows it, and the brute-force
/// ban refuses a source by it. Two opposite disasters sit either side of getting it wrong, and each
/// of these tests stands in front of one of them.
///
/// <para>
/// Believe nginx too little and every request appears to come from <c>127.0.0.1</c>: one budget of
/// attempts shared by everyone on earth, a journal that says only "the server", and a ban that
/// bans the reverse proxy — which locks out every user of the panel at once.
/// </para>
///
/// <para>
/// Believe <c>X-Forwarded-For</c> too much and the caller picks their own identity: an attacker
/// gets a fresh rate-limit partition per request by editing a header, evades a ban the same way,
/// and can point one at an address they chose — the journal then records whoever they named.
/// </para>
///
/// <para>
/// Both tests drive the real <see cref="Program"/> pipeline over HTTP and read the address out of
/// the audit journal afterwards, so they exercise the panel's actual registration
/// (<c>AddPanelForwardedHeaders</c>) and its actual placement (<c>app.UseForwardedHeaders()</c>,
/// first in the pipeline) rather than a configuration re-stated by the test. Deleting either line
/// from <c>Program.cs</c>, or clearing the known-proxy list without re-filling it, turns one of
/// them red.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ForwardedClientAddressTests : IAsyncLifetime
{
    /// <summary>The address nginx would append for the real caller.</summary>
    private const string ClientAddress = "203.0.113.7";

    /// <summary>An address no trusted proxy has: a caller reaching the panel's port directly.</summary>
    private const string StrangerAddress = "198.51.100.4";

    /// <summary>The key the test host uses for both encryption and JWT signing.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The name the refused sign-in attempt is made under; no such user exists.</summary>
    private const string Username = "intruder";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ForwardedClientAddressTests(PostgresFixture postgres)
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

    /// <summary>The address the local proxy forwards is the one the panel records.</summary>
    [Fact]
    public async Task The_address_the_local_proxy_forwards_is_the_one_the_panel_records()
    {
        // nginx is on this machine, so it reaches the panel from loopback and appends the peer it
        // saw with $proxy_add_x_forwarded_for. Recording loopback here would be a ban on nginx.
        await using var factory = CreateFactory(IPAddress.Loopback);
        await MigrateAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ClientAddress);

        var response = await AttemptSignInAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ClientAddress, await RecordedAddressAsync(factory));
    }

    /// <summary>A forwarded address from a caller that is not the proxy is ignored.</summary>
    [Fact]
    public async Task A_forwarded_address_from_a_caller_that_is_not_the_proxy_is_ignored()
    {
        // The header is written by whoever sends it. An attacker who reaches the panel's port
        // without going through nginx must not be able to hand the panel an identity: if they
        // could, a ban would land on the address they named and never on them.
        await using var factory = CreateFactory(IPAddress.Parse(StrangerAddress));
        await MigrateAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ClientAddress);

        var response = await AttemptSignInAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(StrangerAddress, await RecordedAddressAsync(factory));
    }

    /// <summary>Builds a test host that answers from the given peer address.</summary>
    /// <param name="peer">The address the request appears to arrive from, as Kestrel would report it.</param>
    /// <returns>The factory; the caller disposes it.</returns>
    private WebApplicationFactory<Program> CreateFactory(IPAddress peer)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Testing, not Development: inheriting the developer's database settings made these
            // tests pass locally against the wrong database and fail in CI.
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
                services.AddSingleton<IStartupFilter>(new RemotePeerStartupFilter(peer));
            });
        });
    }

    /// <summary>Applies the Identity schema this test's fresh database does not have yet.</summary>
    /// <param name="factory">The test host, whose services hold the module's context.</param>
    /// <returns>Resolves once the journal's table exists.</returns>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// Makes one refused sign-in attempt — the shape of request the ban system exists to count.
    /// </summary>
    /// <param name="client">The client to send it with.</param>
    /// <returns>The response, which is expected to be a 401.</returns>
    private static async Task<HttpResponseMessage> AttemptSignInAsync(HttpClient client)
    {
        return await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Username, Password = "not the password" });
    }

    /// <summary>Reads back the address the panel wrote into the journal for the refused attempt.</summary>
    /// <param name="factory">The test host, whose services hold the module's context.</param>
    /// <returns>The single recorded address.</returns>
    private static async Task<string> RecordedAddressAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var addresses = await context.AuditEvents
            .Where(e => e.Action == AuditActions.LoginFailed)
            .Select(e => e.IpAddress)
            .ToListAsync();

        return Assert.Single(addresses);
    }
}
