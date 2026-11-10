using System.Net;
using System.Net.Http.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The anonymous refresh and logout endpoints are rate limited per address and answer 429 with the
/// panel's shared problem body when the budget is spent (issue #471).
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class SessionRefreshRateLimitTests : IAsyncLifetime
{
    /// <summary>The budget the host is booted with.</summary>
    private const int Budget = 3;

    /// <summary>A key of the right shape; startup validation refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SessionRefreshRateLimitTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>The request after the budget is refused with 429 and the rate-limited code; those inside it are not.</summary>
    /// <remarks>
    /// The boundary and its inverse control in one: requests 1..Budget are answered (204, no cookie
    /// presented), request Budget+1 is not. Removing the attribute turns the last assertion red.
    /// </remarks>
    [Fact]
    public async Task Refresh_requests_beyond_the_budget_are_refused_with_the_rate_limited_code()
    {
        await using var factory = Boot();
        using var client = factory.CreateClient();

        for (var request = 0; request < Budget; request++)
        {
            var allowed = await client.PostAsync("/api/v1/auth/refresh", null);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        var refused = await client.PostAsync("/api/v1/auth/refresh", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("HostRateLimited", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>Logout shares the bucket, so a flood of logouts is refused too.</summary>
    [Fact]
    public async Task Logout_requests_beyond_the_budget_are_refused()
    {
        await using var factory = Boot();
        using var client = factory.CreateClient();

        for (var request = 0; request < Budget; request++)
        {
            var allowed = await client.PostAsync("/api/v1/auth/logout", null);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        var refused = await client.PostAsync("/api/v1/auth/logout", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    /// <summary>The inverse control: another endpoint's budget is untouched by a spent refresh budget.</summary>
    [Fact]
    public async Task A_spent_refresh_budget_does_not_refuse_the_password_reset_endpoint()
    {
        await using var factory = Boot();
        using var client = factory.CreateClient();
        for (var request = 0; request <= Budget; request++)
        {
            await client.PostAsync("/api/v1/auth/refresh", null);
        }

        var other = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { Email = "nobody@example.com" });

        Assert.NotEqual(HttpStatusCode.TooManyRequests, other.StatusCode);
    }

    /// <summary>Boots a host whose refresh budget is <see cref="Budget"/>.</summary>
    /// <returns>The factory, which the caller disposes.</returns>
    private WebApplicationFactory<Program> Boot()
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
            builder.UseSetting("Panel:PublicUrl", "https://panel.example.com");
            builder.UseSetting("RateLimiting:SessionRefreshMaxRequests", Budget.ToString(System.Globalization.CultureInfo.InvariantCulture));

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }
}
