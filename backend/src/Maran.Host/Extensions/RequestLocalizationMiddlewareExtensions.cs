using Maran.Host.Middleware;

namespace Maran.Host.Extensions;

/// <summary>Registers <see cref="RequestLocalizationMiddleware"/> on the pipeline.</summary>
public static class RequestLocalizationMiddlewareExtensions
{
    /// <summary>
    /// Adds <see cref="RequestLocalizationMiddleware"/>. Named <c>UsePanelLocalization</c> rather
    /// than <c>UseRequestLocalization</c> to avoid colliding with ASP.NET Core's own extension
    /// method of that name, which this backend deliberately does not use (the panel's supported
    /// culture set and fallback rule are simple enough to own directly).
    /// </summary>
    /// <param name="app">The application pipeline builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IApplicationBuilder UsePanelLocalization(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestLocalizationMiddleware>();
    }
}
