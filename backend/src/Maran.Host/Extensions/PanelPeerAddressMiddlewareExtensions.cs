using Maran.Host.Middleware;

namespace Maran.Host.Extensions;

/// <summary>Adds <see cref="PanelPeerAddressMiddleware"/> to the request pipeline.</summary>
public static class PanelPeerAddressMiddlewareExtensions
{
    /// <summary>
    /// Applies the socket peer policy and stamps the proxy's address, before anything reads one.
    /// </summary>
    /// <param name="app">The application being built.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>
    /// <b>Must be registered immediately before <c>app.UseForwardedHeaders()</c>.</b> It exists to
    /// feed that middleware the address it compares against <c>KnownProxies</c>; registered after
    /// it, it would be writing down a decision that had already been made without it — and made
    /// wrongly, because a null address makes the forwarded-header check skip rather than fail.
    /// </remarks>
    public static WebApplication UsePanelPeerAddress(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<PanelPeerAddressMiddleware>();
        return app;
    }
}
