using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Maran.Host.IntegrationTests;

/// <summary>Boots the real host against a disposable PostgreSQL.</summary>
public sealed class HostBootTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.StartAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return _pg.DisposeAsync().AsTask();
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
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var agent = body.RootElement.GetProperty("agent").GetString();
        Assert.True(agent is "connected" or "unavailable");
    }
}
