using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The panel's own certificate over real HTTP against real PostgreSQL (issue #417): a panel installed
/// by address is an ordinary state read as a 200 flagged <c>hostNameConfigured: false</c>, while
/// ordering a certificate for it is still refused with its own error.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class PanelCertificateEndpointTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The endpoint under test.</summary>
    private const string Path = "/api/v1/panel-certificate";

    /// <summary>The host name the configured-panel tests boot the host with.</summary>
    private const string PanelName = "panel.example.net";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PanelCertificateEndpointTests(PostgresFixture postgres)
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

    /// <summary>A panel with no host name is read as a 200 flagged so, with the sentence and no certificate.</summary>
    [Fact]
    public async Task A_panel_with_no_host_name_is_read_as_a_200_flagged_so()
    {
        await using var factory = CreateFactory(hostName: null);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.False(root.GetProperty("hostNameConfigured").GetBoolean());
        Assert.Contains("Panel__HostName", root.GetProperty("hostNameNotice").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("hostName").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("expiresAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("issuedAt").ValueKind);
    }

    /// <summary>Ordering a certificate for a panel with no host name is still refused with its own error.</summary>
    [Fact]
    public async Task Ordering_a_certificate_for_a_panel_with_no_host_name_is_still_refused()
    {
        await using var factory = CreateFactory(hostName: null);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsync(Path, content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PanelHostNameNotConfigured", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>A panel with a host name and no recorded certificate is still answered 404, flagged by its code.</summary>
    [Fact]
    public async Task A_panel_with_a_host_name_and_no_recorded_certificate_is_answered_not_issued()
    {
        await using var factory = CreateFactory(PanelName);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PanelCertificateNotIssued", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>A signed in customer, and an anonymous caller, can neither read nor order.</summary>
    [Fact]
    public async Task A_customer_and_an_anonymous_caller_can_neither_read_nor_order()
    {
        await using var factory = CreateFactory(hostName: null);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var anonymous = factory.CreateClient();
        using var customer = await SignInAsync(factory, "customer");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync(Path, content: null)).StatusCode);
    }

    /// <summary>Boots the host against this test's database with the agent's monitor stubbed.</summary>
    /// <param name="hostName">The panel's configured host name, or <c>null</c> for a panel installed by address.</param>
    /// <returns>The host factory.</returns>
    private WebApplicationFactory<Program> CreateFactory(string? hostName)
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

            if (hostName is not null)
            {
                builder.UseSetting("Panel:HostName", hostName);
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
        await scope.ServiceProvider.GetRequiredService<SslDbContext>().Database.MigrateAsync();
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
