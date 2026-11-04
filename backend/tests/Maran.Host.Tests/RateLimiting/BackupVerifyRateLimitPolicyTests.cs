using System.Net;
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
/// Behavioral contract of <see cref="BackupVerifyRateLimitPolicy"/>: the bucket that bounds how many
/// full-archive reads one caller can make the root agent perform. Built as a minimal
/// <see cref="TestServer"/> around the real policy, with the budget configured tiny so the test is
/// fast and deterministic.
/// </summary>
public sealed class BackupVerifyRateLimitPolicyTests
{
    /// <summary>A verification beyond the configured budget is refused with 429.</summary>
    [Fact]
    public async Task A_verification_beyond_the_configured_budget_is_refused_with_429()
    {
        using var host = await BuildHostAsync(maxRequests: 2);
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/verify-probe", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/verify-probe", null)).StatusCode);

        var refused = await client.PostAsync("/verify-probe", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"));
    }

    /// <summary>Verifications within the configured budget all succeed.</summary>
    [Fact]
    public async Task Verifications_within_the_configured_budget_all_succeed()
    {
        using var host = await BuildHostAsync(maxRequests: 3);
        using var client = host.GetTestClient();

        for (var request = 0; request < 3; request++)
        {
            var response = await client.PostAsync("/verify-probe", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    /// <summary>The verification bucket is separate from the general API budget.</summary>
    [Fact]
    public async Task The_verification_bucket_does_not_spend_the_general_api_budget()
    {
        using var host = await BuildHostAsync(maxRequests: 1);
        using var client = host.GetTestClient();

        await client.PostAsync("/verify-probe", null);
        var verifyRefused = await client.PostAsync("/verify-probe", null);
        var api = await client.GetAsync("/api-probe");

        Assert.Equal(HttpStatusCode.TooManyRequests, verifyRefused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
    }

    /// <summary>Builds a minimal test host wiring the real verification policy behind a probe endpoint.</summary>
    /// <param name="maxRequests">Verifications allowed per partition before rejection.</param>
    /// <returns>The started host.</returns>
    private static async Task<IHost> BuildHostAsync(int maxRequests)
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddRouting();
                services.Configure<RateLimitOptions>(options =>
                {
                    options.BackupVerifyMaxRequests = maxRequests;
                    options.BackupVerifyWindowSeconds = 3600;
                });
                services.AddPanelRateLimiting();
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapPost("/verify-probe", () => Results.Ok())
                        .RequireRateLimiting(BackupVerifyRateLimitPolicy.Name);
                    endpoints.MapGet("/api-probe", () => Results.Ok())
                        .RequireRateLimiting(ApiRateLimitPolicy.Name);
                });
            });
        });

        return await hostBuilder.StartAsync();
    }
}
