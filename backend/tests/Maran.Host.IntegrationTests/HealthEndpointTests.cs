using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Covers the readiness case that needs a real, reachable database — the in-memory
/// <c>Maran.Host.Tests</c> project covers the unreachable/not-configured cases without the
/// cost of a container.
/// </summary>
public sealed class HealthEndpointTests : IAsyncLifetime
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

    [Fact]
    public async Task Readiness_endpoint_returns_200_when_the_database_is_reachable()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Testing, not Development: inheriting the developer's database settings made these
            // tests pass locally against the wrong database and fail in CI.
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
            builder.UseSetting("Security:EncryptionKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
            builder.UseSetting("Jwt:SigningKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
