using System.Net;
using Maran.Host.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.Tests.Middleware;

/// <summary>
/// Behavioural contract of the panel's forwarded-header configuration: whose word it takes for
/// the caller's address.
/// </summary>
/// <remarks>
/// This matters twice over. The address is what the login rate limiter partitions on and what the
/// audit journal records, so believing the wrong one is either a lockout of everybody or a
/// limiter an attacker can walk around by setting a header. The second test is the one to keep.
/// </remarks>
public sealed class ForwardedHeadersTests
{
    /// <summary>A forwarded address from the local proxy is believed.</summary>
    [Fact]
    public async Task A_forwarded_address_from_the_local_proxy_is_believed()
    {
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        var seen = await client.GetStringAsync("/whoami");

        Assert.Equal("203.0.113.7", seen);
    }

    /// <summary>A forwarded address from an untrusted caller is ignored.</summary>
    [Fact]
    public async Task A_forwarded_address_from_an_untrusted_caller_is_ignored()
    {
        // The header is written by the client. If it were honoured from anywhere, an attacker
        // would get a fresh rate-limit partition per request simply by changing it — the same
        // defect that made the login limiter useless when it keyed on a query parameter.
        using var host = await BuildHostAsync(remoteAddress: IPAddress.Parse("198.51.100.4"));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        var seen = await client.GetStringAsync("/whoami");

        Assert.Equal("198.51.100.4", seen);
    }

    /// <summary>Only one hop is taken, so a chain the proxy was handed is not believed.</summary>
    [Fact]
    public async Task Only_one_hop_is_taken_from_a_forwarded_chain()
    {
        // nginx appends the peer to whatever arrived, so a client that sends its own chain gets
        // "<claimed>, <real>". Taking one hop means the panel reads the address nginx observed.
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.0.0.1, 203.0.113.7");

        var seen = await client.GetStringAsync("/whoami");

        Assert.Equal("203.0.113.7", seen);
    }

    /// <summary>Builds a host with the panel's forwarded-header configuration and a probe endpoint.</summary>
    /// <param name="remoteAddress">The address the request appears to arrive from.</param>
    /// <returns>The started host.</returns>
    private static async Task<IHost> BuildHostAsync(IPAddress? remoteAddress = null)
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddPanelForwardedHeaders();
            });
            webBuilder.Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    context.Connection.RemoteIpAddress = remoteAddress ?? IPAddress.Loopback;
                    await next(context);
                });
                app.UseForwardedHeaders();
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/whoami", (HttpContext context) =>
                    {
                        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    });
                });
            });
        });

        return await hostBuilder.StartAsync();
    }
}
