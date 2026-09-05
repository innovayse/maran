using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the socket peer Kestrel would report, so a test can choose whether the panel is
/// being addressed by the local reverse proxy or by a stranger connecting to it directly.
/// </summary>
/// <remarks>
/// The in-memory test server has no sockets, so nothing meaningful sets
/// <c>HttpContext.Connection.RemoteIpAddress</c> — yet that value is the entire input to the
/// forwarded-header trust decision: it is what is matched against <c>KnownProxies</c>.
///
/// <para>
/// A startup filter is used rather than an ordinary <c>Use</c> registration because it is the only
/// place a component can be inserted <b>ahead of the application's own pipeline</b>. That is where
/// Kestrel's answer would already be by the time <c>app.UseForwardedHeaders()</c> — the first line
/// of the panel's pipeline — runs. Anything registered later would be reading the decision instead
/// of feeding it, and a test built that way would pass whatever the configuration said.
/// </para>
/// </remarks>
public sealed class RemotePeerStartupFilter : IStartupFilter
{
    /// <summary>The address the request is made to appear to arrive from.</summary>
    private readonly IPAddress _peer;

    /// <summary>Creates the filter.</summary>
    /// <param name="peer">The address the request is made to appear to arrive from.</param>
    public RemotePeerStartupFilter(IPAddress peer)
    {
        _peer = peer;
    }

    /// <summary>Prepends the peer-stamping component to the application's pipeline.</summary>
    /// <param name="next">The pipeline configured by the application and any later filter.</param>
    /// <returns>A configuration action that stamps the peer, then runs the application's pipeline.</returns>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (HttpContext context, RequestDelegate proceed) =>
            {
                context.Connection.RemoteIpAddress = _peer;
                await proceed(context);
            });

            next(app);
        };
    }
}
