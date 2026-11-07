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
/// Behavioral contract of <see cref="AuditExportRateLimitPolicy"/>: the bucket that bounds how fast one
/// administrator can take the security record out of the panel. Built as a minimal
/// <see cref="TestServer"/> around the real policy, with the budget configured tiny so the test is fast
/// and deterministic; the caller's identity is set from a header by a stand-in for authentication.
/// </summary>
public sealed class AuditExportRateLimitPolicyTests
{
    /// <summary>The header the stand-in authentication reads the user id from.</summary>
    private const string UserHeader = "X-Test-User";

    /// <summary>The header the stand-in authentication reads the hosting account id from.</summary>
    private const string AccountHeader = "X-Test-Account";

    /// <summary>An export beyond the configured budget is refused with 429 and a Retry-After.</summary>
    [Fact]
    public async Task An_export_beyond_the_configured_budget_is_refused_with_429()
    {
        using var host = await BuildHostAsync(maxRequests: 2);
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "admin-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "admin-1")).StatusCode);

        var refused = await GetAsync(client, "admin-1");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"));
    }

    /// <summary>The budget is the administrator's: a second administrator has a budget of their own.</summary>
    [Fact]
    public async Task A_second_administrator_has_a_budget_of_their_own()
    {
        using var host = await BuildHostAsync(maxRequests: 1);
        using var client = host.GetTestClient();

        await GetAsync(client, "admin-1");
        var firstRefused = await GetAsync(client, "admin-1");
        var second = await GetAsync(client, "admin-2");

        Assert.Equal(HttpStatusCode.TooManyRequests, firstRefused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>
    /// A hosting-account claim does not split or merge the budget: the same user with two different
    /// account claims shares one allowance, because the partition is the person.
    /// </summary>
    [Fact]
    public async Task An_account_claim_does_not_give_the_same_user_a_second_budget()
    {
        using var host = await BuildHostAsync(maxRequests: 1);
        using var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "admin-1", account: "acct-1")).StatusCode);

        var otherAccount = await GetAsync(client, "admin-1", account: "acct-2");

        Assert.Equal(HttpStatusCode.TooManyRequests, otherAccount.StatusCode);
    }

    /// <summary>The export bucket does not spend the general API budget.</summary>
    [Fact]
    public async Task The_export_bucket_does_not_spend_the_general_api_budget()
    {
        using var host = await BuildHostAsync(maxRequests: 1);
        using var client = host.GetTestClient();

        await GetAsync(client, "admin-1");
        var exportRefused = await GetAsync(client, "admin-1");
        var api = await client.GetAsync("/api-probe");

        Assert.Equal(HttpStatusCode.TooManyRequests, exportRefused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
    }

    /// <summary>Sends one export request as the named user.</summary>
    /// <param name="client">The test client.</param>
    /// <param name="user">The user id to authenticate as.</param>
    /// <param name="account">The hosting account claim to carry, or null for none.</param>
    /// <returns>The panel's answer.</returns>
    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string user, string? account = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/export-probe");
        request.Headers.Add(UserHeader, user);
        if (account is not null)
        {
            request.Headers.Add(AccountHeader, account);
        }

        return client.SendAsync(request);
    }

    /// <summary>Builds a minimal test host wiring the real export policy behind a probe endpoint.</summary>
    /// <param name="maxRequests">Exports allowed per administrator before rejection.</param>
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
                    options.AuditExportMaxRequests = maxRequests;
                    options.AuditExportWindowSeconds = 3600;
                });
                services.AddPanelRateLimiting();
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.Use(async (context, next) =>
                {
                    var claims = new List<Claim>();
                    if (context.Request.Headers.TryGetValue(UserHeader, out var user))
                    {
                        claims.Add(new Claim(PanelClaimTypes.UserId, user.ToString()));
                    }

                    if (context.Request.Headers.TryGetValue(AccountHeader, out var account))
                    {
                        claims.Add(new Claim(PanelClaimTypes.AccountId, account.ToString()));
                    }

                    context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
                    await next();
                });
                app.UseRateLimiter();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/export-probe", () => Results.Ok())
                        .RequireRateLimiting(AuditExportRateLimitPolicy.Name);
                    endpoints.MapGet("/api-probe", () => Results.Ok())
                        .RequireRateLimiting(ApiRateLimitPolicy.Name);
                });
            });
        });

        return await hostBuilder.StartAsync();
    }
}
