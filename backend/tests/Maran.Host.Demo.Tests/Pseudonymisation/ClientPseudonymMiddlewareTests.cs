using System.Net;
using Maran.Host.Demo.Pseudonymisation;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.Http;

namespace Maran.Host.Demo.Tests.Pseudonymisation;

/// <summary>After the middleware, nothing a later component reads is real.</summary>
public sealed class ClientPseudonymMiddlewareTests
{
    /// <summary>The real address the tests plant.</summary>
    private const string RealAddress = "203.0.113.9";

    /// <summary>The real user agent the tests plant.</summary>
    private const string RealUserAgent =
        "Mozilla/5.0 (X11; Linux x86_64; rv:132.0) Gecko/20100101 Firefox/132.0 SecretBuild/1.2.3";

    /// <summary>A request carrying the real address, in every place it can sit.</summary>
    /// <returns>The context.</returns>
    private static DefaultHttpContext RequestFromRealVisitor()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(RealAddress);
        context.Request.Headers["X-Forwarded-For"] = RealAddress;
        context.Request.Headers["X-Original-For"] = RealAddress;
        context.Request.Headers["X-Real-IP"] = RealAddress;
        context.Request.Headers["Forwarded"] = $"for={RealAddress}";
        context.Request.Headers["CF-Connecting-IP"] = RealAddress;
        context.Request.Headers["True-Client-IP"] = RealAddress;
        context.Request.Headers["X-Client-IP"] = RealAddress;
        context.Request.Headers["X-Cluster-Client-IP"] = RealAddress;
        context.Request.Headers["Fastly-Client-IP"] = RealAddress;
        context.Request.Headers.UserAgent = RealUserAgent;
        return context;
    }

    /// <summary>
    /// The positive control: with the middleware absent the probe finds the real address, so a clean result means
    /// something.
    /// </summary>
    [Fact]
    public void Without_the_middleware_the_real_address_and_agent_are_what_later_code_reads()
    {
        var context = RequestFromRealVisitor();

        Assert.Equal(RealAddress, ClientAddress.Of(context.Connection.RemoteIpAddress));
        Assert.Contains("SecretBuild", context.Request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// With the middleware, the address, every header carrying it and the agent are replaced or gone.
    /// </summary>
    [Fact]
    public async Task The_real_address_and_agent_are_gone_after_the_middleware()
    {
        var context = RequestFromRealVisitor();
        var middleware = new ClientPseudonymMiddleware(_ => { return Task.CompletedTask; }, new ClientPseudonymiser());

        await middleware.InvokeAsync(context);

        var address = ClientAddress.Of(context.Connection.RemoteIpAddress);
        Assert.DoesNotContain(RealAddress, address, StringComparison.Ordinal);
        Assert.StartsWith("2001:db8:", address, StringComparison.Ordinal);
        foreach (var header in ClientPseudonymMiddleware.AddressHeaders)
        {
            Assert.False(context.Request.Headers.ContainsKey(header), $"{header} still present.");
        }

        Assert.Equal("Firefox", context.Request.Headers.UserAgent.ToString());
    }

    /// <summary>The next component runs after the rewrite, seeing the stand-in.</summary>
    [Fact]
    public async Task The_next_component_sees_the_stand_in()
    {
        var context = RequestFromRealVisitor();
        string? seen = null;
        var middleware = new ClientPseudonymMiddleware(
            next =>
            {
                seen = ClientAddress.Of(next.Connection.RemoteIpAddress);
                return Task.CompletedTask;
            },
            new ClientPseudonymiser());

        await middleware.InvokeAsync(context);

        Assert.NotNull(seen);
        Assert.StartsWith("2001:db8:", seen, StringComparison.Ordinal);
    }

    /// <summary>A connection that reports no address is left without one.</summary>
    [Fact]
    public async Task A_connection_with_no_address_stays_without_one()
    {
        var context = new DefaultHttpContext();
        var middleware = new ClientPseudonymMiddleware(_ => { return Task.CompletedTask; }, new ClientPseudonymiser());

        await middleware.InvokeAsync(context);

        Assert.Null(context.Connection.RemoteIpAddress);
        Assert.Equal(ClientAddress.Unknown, ClientAddress.Of(context.Connection.RemoteIpAddress));
    }
}
