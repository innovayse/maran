using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Maran.Host.DemoIntegrationTests.Fixtures;

/// <summary>
/// Gives every request a remote address before anything in the demo pipeline runs, as the real proxy chain
/// does: the address in the <c>X-Test-Peer</c> header when a test sends one, else the default. The in-memory
/// test server reports none, and without an address there is nothing to pseudonymise.
/// </summary>
public sealed class FixedPeerStartupFilter : IStartupFilter
{
    /// <summary>The header a test sends to be a different visitor.</summary>
    public const string HeaderName = "X-Test-Peer";

    /// <summary>The address the test pretends the visitor has when it names none.</summary>
    private readonly IPAddress _peer;

    /// <summary>Creates the filter.</summary>
    /// <param name="peer">The address every request appears to come from.</param>
    public FixedPeerStartupFilter(IPAddress peer)
    {
        _peer = peer;
    }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use((context, pipeline) =>
            {
                var named = context.Request.Headers[HeaderName].ToString();
                context.Connection.RemoteIpAddress = IPAddress.TryParse(named, out var parsed) ? parsed : _peer;
                return pipeline();
            });
            next(app);
        };
    }
}
