using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Monitoring.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The panel's runtime log level over real HTTP against real PostgreSQL (issue #131): administrators
/// only, effective without a restart, journalled, and applied again when the panel starts.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class PanelLogLevelEndpointTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The endpoint under test.</summary>
    private const string Path = "/api/v1/log-level";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PanelLogLevelEndpointTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller can neither read nor change the level.</summary>
    [Fact]
    public async Task An_anonymous_caller_can_neither_read_nor_change_the_level()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(Path, new { Level = "debug" })).StatusCode);
    }

    /// <summary>A signed in customer can neither read nor change the level, and the logger is untouched.</summary>
    [Fact]
    public async Task A_signed_in_customer_can_neither_read_nor_change_the_level()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");
        var before = factory.Services.GetRequiredService<IPanelLogLevelSwitch>().Current;

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(Path, new { Level = "verbose" })).StatusCode);
        Assert.Equal(before, factory.Services.GetRequiredService<IPanelLogLevelSwitch>().Current);
    }

    /// <summary>An administrator's change takes effect at once, is reported back, and is journalled.</summary>
    [Fact]
    public async Task An_administrators_change_takes_effect_at_once_is_reported_back_and_is_journalled()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PutAsJsonAsync(Path, new { Level = "warning" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PanelLogLevel.Warning, factory.Services.GetRequiredService<IPanelLogLevelSwitch>().Current);

        using var body = JsonDocument.Parse(await client.GetStringAsync(Path));
        Assert.Equal("warning", body.RootElement.GetProperty("level").GetString());
        Assert.Equal(6, body.RootElement.GetProperty("choices").GetArrayLength());

        using var scope = factory.Services.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
            .AsNoTracking()
            .Where(entry => entry.Action == AuditActions.PanelLogLevelChanged)
            .ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal("admin", entry.ActorUsername);
        Assert.EndsWith("-> Warning", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A number that names no level is refused with the module's code and changes nothing.</summary>
    [Fact]
    public async Task A_number_that_names_no_level_is_refused_and_changes_nothing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        var before = factory.Services.GetRequiredService<IPanelLogLevelSwitch>().Current;

        var response = await client.PutAsJsonAsync(Path, new { Level = 99 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("LogLevelUnknown", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, factory.Services.GetRequiredService<IPanelLogLevelSwitch>().Current);
    }

    /// <summary>A stored level is applied again when the panel next starts.</summary>
    [Fact]
    public async Task A_stored_level_is_applied_again_when_the_panel_next_starts()
    {
        await using (var first = CreateFactory())
        {
            await MigrateAsync(first);
            await SeedUsersAsync(first);
            using var client = await SignInAsync(first, "admin");
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Path, new { Level = "error" })).StatusCode);
        }

        // Reading Services starts the host, which runs every hosted service's StartAsync first.
        await using var second = CreateFactory();

        Assert.Equal(PanelLogLevel.Error, second.Services.GetRequiredService<IPanelLogLevelSwitch>().Current);
    }

    /// <summary>Boots the host against this test's database with the agent's monitor stubbed.</summary>
    /// <returns>The host factory.</returns>
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
