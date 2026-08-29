using Maran.Host.Middleware;

namespace Maran.Host.Extensions;

/// <summary>Registers <see cref="CorrelationIdMiddleware"/> on the pipeline.</summary>
public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>
    /// Adds <see cref="CorrelationIdMiddleware"/>. Must run before <c>UseExceptionHandling</c> and
    /// before anything that logs, so every downstream component sees the correlation id already set.
    /// </summary>
    /// <param name="app">The application pipeline builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
