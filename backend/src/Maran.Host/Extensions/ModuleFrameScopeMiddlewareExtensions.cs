using Maran.Host.Middleware;

namespace Maran.Host.Extensions;

/// <summary>Adds <see cref="ModuleFrameScopeMiddleware"/> to the request pipeline.</summary>
public static class ModuleFrameScopeMiddlewareExtensions
{
    /// <summary>
    /// Holds a call the shell relays for a module's screen to that module's own endpoints. Must come after
    /// routing and authentication, so the matched endpoint is known, and before the endpoint runs.
    /// </summary>
    /// <param name="app">The application being built.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseModuleFrameScope(this WebApplication app)
    {
        app.UseMiddleware<ModuleFrameScopeMiddleware>();
        return app;
    }
}
