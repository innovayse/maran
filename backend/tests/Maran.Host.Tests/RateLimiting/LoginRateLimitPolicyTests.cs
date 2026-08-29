using System.Net;
using System.Text.Json;
using Maran.Host.Configuration;
using Maran.Host.Extensions;
using Maran.Host.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
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
        Assert.Equal("host.rate_limited", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// DEFECT (reported, not fixed — see crosscutting-tests-report.md): the shared rejection
    /// handler in <see cref="RateLimitingExtensions.AddPanelRateLimiting"/> reads
    /// <c>MetadataName.RetryAfter</c> off the rejected lease and writes it as the <c>Retry-After</c>
    /// header, matching its own stated intent ("a rejected caller must learn when to come back").
    /// For the "api" policy (<see cref="ApiRateLimitPolicy"/>, a <c>FixedWindowRateLimiter</c>) that
    /// metadata is present and the header appears. For the "login" policy under test here — the
    /// same <c>SlidingWindowRateLimiterOptions</c> shape production uses, just with a shrunk window
    /// for test speed — the BCL's <c>SlidingWindowRateLimiter</c> does not populate that metadata
    /// with <c>QueueLimit = 0</c>, so no header is ever sent for a rejected login attempt. This
    /// assertion documents the intended, currently-unmet behavior rather than being weakened to
    /// match the gap.
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

    [Fact]
    public async Task Distinct_partitions_do_not_share_a_budget()
    {
        using var host = await BuildHostAsync(maxAttempts: 1);
        using var client = host.GetTestClient();

        var first = await client.GetAsync($"/login-probe?username=user-{Guid.NewGuid():N}");
        var second = await client.GetAsync($"/login-probe?username=user-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
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
                    endpoints.MapGet("/login-probe", () => Results.Ok())
                        .RequireRateLimiting(LoginRateLimitPolicy.Name));
            });
        });

        return await hostBuilder.StartAsync();
    }
}
