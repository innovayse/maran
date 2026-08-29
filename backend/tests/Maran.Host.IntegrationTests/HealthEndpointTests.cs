using System.Net;
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
    public Task InitializeAsync() => _pg.StartAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    [Fact]
    public async Task Readiness_endpoint_returns_200_when_the_database_is_reachable()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Panel", _pg.GetConnectionString());
            builder.UseSetting("Security:EncryptionKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
