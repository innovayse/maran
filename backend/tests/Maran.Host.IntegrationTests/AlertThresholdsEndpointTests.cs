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
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The alert thresholds over real HTTP against real PostgreSQL (issue #142): administrators only,
/// bounded server-side, stored in the one row and journalled with the figures before and after.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AlertThresholdsEndpointTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The endpoint under test.</summary>
    private const string Path = "/api/v1/monitoring/alert-thresholds";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AlertThresholdsEndpointTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller can neither read nor change the thresholds.</summary>
    [Fact]
    public async Task An_anonymous_caller_can_neither_read_nor_change_the_thresholds()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Path)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync(Path, new { DiskPercent = 80 })).StatusCode);
    }

    /// <summary>A signed in customer can neither read nor change the thresholds, and nothing is stored.</summary>
    [Fact]
    public async Task A_signed_in_customer_can_neither_read_nor_change_the_thresholds()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Path)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(Path, new { DiskPercent = 60, CpuPercent = 60, MemoryPercent = 60 })).StatusCode);

        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().AlertThresholds.ToListAsync());
    }

    /// <summary>A panel nobody configured reports ninety for the disk and the other two unwatched.</summary>
    [Fact]
    public async Task A_panel_nobody_configured_reports_ninety_for_the_disk_and_the_other_two_unwatched()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        using var body = JsonDocument.Parse(await client.GetStringAsync(Path));

        Assert.Equal(90, body.RootElement.GetProperty("diskPercent").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("cpuPercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("memoryPercent").ValueKind);
        Assert.Equal(50, body.RootElement.GetProperty("minimumPercent").GetInt32());
        Assert.Equal(99, body.RootElement.GetProperty("maximumPercent").GetInt32());
    }

    /// <summary>An administrator's save is reported back, stored in the one row and journalled with old and new figures.</summary>
    [Fact]
    public async Task An_administrators_save_is_reported_back_stored_and_journalled_with_old_and_new_figures()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PutAsJsonAsync(Path, new { DiskPercent = 85, CpuPercent = 70, MemoryPercent = (int?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var body = JsonDocument.Parse(await client.GetStringAsync(Path));
        Assert.Equal(85, body.RootElement.GetProperty("diskPercent").GetInt32());
        Assert.Equal(70, body.RootElement.GetProperty("cpuPercent").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("memoryPercent").ValueKind);

        using var scope = factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().AlertThresholds.ToListAsync());
        var entries = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
            .AsNoTracking()
            .Where(entry => entry.Action == MonitoringAuditActions.AlertThresholdsSaved)
            .ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal("disk=90;cpu=off;memory=off -> disk=85;cpu=70;memory=off", entry.Subject);
    }

    /// <summary>A figure outside the bounds is refused with 400 and the module's code, and changes nothing.</summary>
    [Theory]
    [InlineData(49, null, null)]
    [InlineData(100, null, null)]
    [InlineData(90, 49, null)]
    [InlineData(90, null, 100)]
    public async Task A_figure_outside_the_bounds_is_refused_and_changes_nothing(int disk, int? cpu, int? memory)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PutAsJsonAsync(
            Path, new { DiskPercent = disk, CpuPercent = cpu, MemoryPercent = memory });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AlertThresholdOutOfRange", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().AlertThresholds.ToListAsync());
    }

    /// <summary>Boots the host against this test's database with the agent's monitor stubbed.</summary>
    /// <returns>The host factory.</returns>
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
