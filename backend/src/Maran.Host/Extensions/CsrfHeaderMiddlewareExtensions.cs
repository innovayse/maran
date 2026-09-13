using Maran.Host.Middleware;

namespace Maran.Host.Extensions;

/// <summary>Adds <see cref="CsrfHeaderMiddleware"/> to the request pipeline.</summary>
public static class CsrfHeaderMiddlewareExtensions
{
    /// <summary>Requires the panel's custom header on cookie-bearing state changes.</summary>
    /// <param name="app">The application being built.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseCsrfHeader(this WebApplication app)
    {
        app.UseMiddleware<CsrfHeaderMiddleware>();
        return app;
    }
}
