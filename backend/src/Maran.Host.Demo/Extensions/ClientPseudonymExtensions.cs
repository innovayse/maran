using Maran.Host.Demo.Pseudonymisation;

namespace Maran.Host.Demo.Extensions;

/// <summary>Adds <see cref="ClientPseudonymMiddleware"/> to the request pipeline.</summary>
public static class ClientPseudonymExtensions
{
    /// <summary>Replaces the caller's address and user agent with their stand-ins.</summary>
    /// <param name="app">The application being built.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>
    /// <b>Must come immediately after <c>UseForwardedHeaders</c></b> and before request logging, the
    /// correlation id, rate limiting and authentication: an address anything reads or logs before this
    /// runs is the real one. <c>AddDemoGuard</c> registers its services.
    /// </remarks>
    public static WebApplication UseClientPseudonym(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<ClientPseudonymMiddleware>();
        return app;
    }
}
