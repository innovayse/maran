using Maran.Host.Middleware;

namespace Maran.Host.Extensions;

/// <summary>Registers <see cref="ExceptionMiddleware"/> on the pipeline.</summary>
public static class ExceptionMiddlewareExtensions
{
    /// <summary>
    /// Adds <see cref="ExceptionMiddleware"/>. Must run after <c>UseCorrelationId</c> (so the
    /// problem response can carry the id) and as early as possible otherwise, so it wraps every
    /// other component.
    /// </summary>
    /// <param name="app">The application pipeline builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionMiddleware>();
}
