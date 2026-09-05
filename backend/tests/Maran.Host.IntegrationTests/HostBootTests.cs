using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.IntegrationTests;

/// <summary>Boots the real host against a disposable PostgreSQL.</summary>
[Collection(SharedDatabase.Name)]
public sealed class HostBootTests : IAsyncLifetime
{
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public HostBootTests(PostgresFixture postgres)
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

    /// <summary>Host boots with postgres and serves health.</summary>
    [Fact]
    public async Task Host_boots_with_postgres_and_serves_health()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Testing, not Development: appsettings.Development.json points at a developer's own
            // PostgreSQL, and inheriting it made these tests pass locally by accident while
            // connecting to the wrong database — and fail in CI, where nothing listens there.
            b.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                b.UseSetting(setting.Key, setting.Value);
            }
            // Startup validation refuses to boot without an encryption key (rules/security.md).
            b.UseSetting("Security:EncryptionKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
            b.UseSetting("Jwt:SigningKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                b.UseSetting(setting.Key, setting.Value);
            }
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var agent = body.RootElement.GetProperty("agent").GetString();
        Assert.True(agent is "connected" or "unavailable");
    }
}
