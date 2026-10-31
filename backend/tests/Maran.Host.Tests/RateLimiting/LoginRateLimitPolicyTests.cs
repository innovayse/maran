using System.Net;
using System.Text.Json;
using Maran.Host.Configuration;
using Maran.Host.Extensions;
using Maran.Host.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.Tests.RateLimiting;

/// <summary>
/// Behavioral contract of <see cref="LoginRateLimitPolicy"/> and its rejection wiring in
/// <see cref="RateLimitingExtensions"/>. Built as a minimal <see cref="TestServer"/> around the
/// real policy and extension rather than through the full <see cref="Program"/> host, because no
/// authentication endpoint has shipped yet to enable the <c>login</c> policy on
/// (rules/security.md "Rate limiting is mandatory on authentication" — the policy is registered
/// ahead of that endpoint). The limits are configured tiny so the test is fast and deterministic.
/// </summary>
public sealed class LoginRateLimitPolicyTests
{
    /// <summary>Attempt beyond the configured budget is rejected with 429 and problem body.</summary>
    [Fact]
    public async Task Attempt_beyond_the_configured_budget_is_rejected_with_429_and_problem_body()
    {
        using var host = await BuildHostAsync(maxAttempts: 2);
        using var client = host.GetTestClient();
        var username = $"user-{Guid.NewGuid():N}";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var allowed = await client.GetAsync($"/login-probe?username={username}");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var rejected = await client.GetAsync($"/login-probe?username={username}");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("HostRateLimited", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// A rejected login attempt carries a <c>Retry-After</c> header. The BCL's sliding-window limiter
    /// publishes no <c>MetadataName.RetryAfter</c> on a refused lease, so the header comes from the
    /// configured lockout through <see cref="RateLimitingExtensions.AddPanelRateLimiting"/>'s fallback.
    /// </summary>
    [Fact]
    public async Task Login_rejection_carries_a_retry_after_header()
    {
        using var host = await BuildHostAsync(maxAttempts: 1);
        using var client = host.GetTestClient();
        var username = $"user-{Guid.NewGuid():N}";

        await client.GetAsync($"/login-probe?username={username}");
        var rejected = await client.GetAsync($"/login-probe?username={username}");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
    }

    /// <summary>Attempts within the configured budget all succeed.</summary>
    [Fact]
    public async Task Attempts_within_the_configured_budget_all_succeed()
    {
        using var host = await BuildHostAsync(maxAttempts: 3);
        using var client = host.GetTestClient();
        var username = $"user-{Guid.NewGuid():N}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await client.GetAsync($"/login-probe?username={username}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    /// <summary>Changing the request cannot buy the caller a fresh budget.</summary>
    /// <remarks>
    /// This test used to assert the opposite, and the policy used to satisfy it: the partition key
    /// mixed in the <c>username</c> QUERY value, while the login endpoint authenticates the
    /// username in the request BODY. An attacker could post the real account's name in the body
    /// and a different random query value every time, landing each attempt in its own partition —
    /// unlimited guesses against one account from one address, with the limiter reporting itself
    /// as working. A budget the caller can reset is not a budget, so the key is now the address
    /// alone and this test names the property that actually matters.
    /// </remarks>
    [Fact]
    public async Task Changing_the_request_cannot_buy_the_caller_a_fresh_budget()
    {
        using var host = await BuildHostAsync(maxAttempts: 1);
        using var client = host.GetTestClient();

        var first = await client.GetAsync($"/login-probe?username=user-{Guid.NewGuid():N}");
        var second = await client.GetAsync($"/login-probe?username=user-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    /// <summary>Builds a minimal test host wiring the real login policy behind a probe endpoint.</summary>
    /// <param name="maxAttempts">Attempts allowed per partition before rejection.</param>
    private static async Task<IHost> BuildHostAsync(int maxAttempts)
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddRouting();
                services.Configure<RateLimitOptions>(options =>
                {
                    options.LoginMaxAttempts = maxAttempts;
                    options.LoginWindowSeconds = 30;
                    options.LoginLockoutSeconds = 300;
                });
                services.AddPanelRateLimiting();
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/login-probe", () => Results.Ok())
                        .RequireRateLimiting(LoginRateLimitPolicy.Name);
                });
            });
        });

        return await hostBuilder.StartAsync();
    }
}
