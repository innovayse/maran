using System.Net;
using System.Security.Claims;
using Maran.Host.Configuration;
using Maran.Host.Extensions;
using Maran.Host.RateLimiting;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.Tests.RateLimiting;

/// <summary>
/// Behavioral contract of <see cref="ProvisioningRateLimitPolicy"/>: the bucket that bounds how fast one
/// API key may write hosting accounts. Built as a minimal <see cref="TestServer"/> around the real
/// policy, with the budget configured tiny so the test is fast and deterministic; the caller's identity
/// is set from headers by a stand-in for authentication.
/// </summary>
public sealed class ProvisioningRateLimitPolicyTests
{
    /// <summary>The header the stand-in authentication reads the API key id from.</summary>
    private const string KeyHeader = "X-Test-Key";

    /// <summary>The header naming the authentication type the stand-in principal carries.</summary>
    private const string SchemeHeader = "X-Test-Scheme";

    /// <summary>A write beyond the configured budget is refused with 429 and a Retry-After.</summary>
    [Fact]
    public async Task A_write_beyond_the_configured_budget_is_refused_with_429()
    {
        using var host = await BuildHostAsync(maxRequests: 2);
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, "key-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, "key-1")).StatusCode);

        var refused = await PostAsync(client, "key-1");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"));
    }

    /// <summary>The budget is the key's: a second key from the same address has a budget of its own.</summary>
    [Fact]
    public async Task A_second_key_from_the_same_address_has_a_budget_of_its_own()
    {
        using var host = await BuildHostAsync(maxRequests: 1);
        using var client = host.GetTestClient();

        await PostAsync(client, "key-1");
        var firstRefused = await PostAsync(client, "key-1");
        var second = await PostAsync(client, "key-2");

        Assert.Equal(HttpStatusCode.TooManyRequests, firstRefused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>The provisioning bucket does not spend the general API budget, and the reverse.</summary>
    [Fact]
    public async Task The_provisioning_bucket_is_independent_of_the_general_api_budget()
    {
        using var host = await BuildHostAsync(maxRequests: 1);
        using var client = host.GetTestClient();

        await PostAsync(client, "key-1");
        var provisioningRefused = await PostAsync(client, "key-1");
        var api = await client.GetAsync("/api-probe");

        Assert.Equal(HttpStatusCode.TooManyRequests, provisioningRefused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
    }

    /// <summary>
    /// A principal that merely carries a key id claim, without having been authenticated by the key scheme,
    /// is not partitioned by that key and so cannot spend the real key's budget.
    /// </summary>
    [Fact]
    public async Task A_principal_not_authenticated_by_the_key_scheme_cannot_spend_a_keys_budget()
    {
        using var host = await BuildHostAsync(maxRequests: 1);
        using var client = host.GetTestClient();

        await PostAsync(client, "key-1");
        var keyRefused = await PostAsync(client, "key-1");
        var forged = await PostAsync(client, "key-1", scheme: "Bearer");

        Assert.Equal(HttpStatusCode.TooManyRequests, keyRefused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, forged.StatusCode);
    }

    /// <summary>The shipped default is sixty writes a minute per key.</summary>
    [Fact]
    public void The_shipped_default_is_sixty_writes_a_minute()
    {
        var options = new RateLimitOptions();

        Assert.Equal(60, options.ProvisioningMaxRequests);
        Assert.Equal(60, options.ProvisioningWindowSeconds);
    }

    /// <summary>Sends one write as the named key.</summary>
    /// <param name="client">The test client.</param>
    /// <param name="key">The key id to authenticate as.</param>
    /// <param name="scheme">The authentication type the principal carries.</param>
    /// <returns>The panel's answer.</returns>
    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string key, string scheme = ApiKeyDefaults.Scheme)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/provision-probe");
        request.Headers.Add(KeyHeader, key);
        request.Headers.Add(SchemeHeader, scheme);

        return client.SendAsync(request);
    }

    /// <summary>Builds a minimal test host wiring the real provisioning policy behind a probe endpoint.</summary>
    /// <param name="maxRequests">Writes allowed per key before rejection.</param>
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
                    options.ProvisioningMaxRequests = maxRequests;
                    options.ProvisioningWindowSeconds = 3600;
                });
                services.AddPanelRateLimiting();
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.Use(async (context, next) =>
                {
                    var claims = new List<Claim>();
                    if (context.Request.Headers.TryGetValue(KeyHeader, out var key))
                    {
                        claims.Add(new Claim(PanelClaimTypes.ApiKeyId, key.ToString()));
                    }

                    var scheme = context.Request.Headers.TryGetValue(SchemeHeader, out var named)
                        ? named.ToString()
                        : "test";
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
                    await next();
                });
                app.UseRateLimiter();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapPost("/provision-probe", () => Results.Ok())
                        .RequireRateLimiting(ProvisioningRateLimitPolicy.Name);
                    endpoints.MapGet("/api-probe", () => Results.Ok())
                        .RequireRateLimiting(ApiRateLimitPolicy.Name);
                });
            });
        });

        return await hostBuilder.StartAsync();
    }
}
