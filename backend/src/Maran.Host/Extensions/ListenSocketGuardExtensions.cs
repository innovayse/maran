using Maran.Host.Configuration;
using Maran.Host.Security;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Options;

namespace Maran.Host.Extensions;

/// <summary>Arms <see cref="ListenSocketGuard"/> for the moment the server finishes binding.</summary>
public static class ListenSocketGuardExtensions
{
    /// <summary>
    /// Restricts the panel's listening socket to the reverse proxy once the server has started.
    /// </summary>
    /// <param name="app">The application being built.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>
    /// Registered among the <c>Use…</c> calls because that is where it belongs in the story of a
    /// request, though it adds no middleware: it hooks <c>ApplicationStarted</c>, the first moment
    /// the socket exists and the bound addresses can be read back instead of assumed.
    /// </remarks>
    public static WebApplication UsePanelListenSocketGuard(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var guard = new ListenSocketGuard(
            app.Services.GetRequiredService<IServer>(),
            app.Lifetime,
            app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>(),
            app.Services.GetRequiredService<ILogger<ListenSocketGuard>>());

        app.Lifetime.ApplicationStarted.Register(guard.Apply);
        return app;
    }
}
