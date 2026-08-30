namespace Maran.Host.Extensions;

/// <summary>
/// The response headers that constrain what a browser will do with the panel (spec §10: strict CSP
/// and security headers).
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Adds the headers to every response, including error responses.
    /// </summary>
    /// <remarks>
    /// Registered first in the pipeline for exactly that reason: a 500 rendered by the exception
    /// middleware is still a response a browser will interpret, and headers added only on the happy
    /// path are missing from precisely the responses an attacker is trying to produce.
    ///
    /// The policy allows scripts and styles only from the panel's own origin — the SPA is served
    /// from it, and nothing else is — and forbids framing outright, which is what stops a
    /// clickjacking page from putting the panel's controls under an invisible overlay.
    /// </remarks>
    /// <param name="app">The application being built.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            // 'unsafe-inline' for styles only: Vue injects component styles as inline <style>
            // elements, and there is no build-time hash to allow instead. Scripts get no such
            // exemption, which is where an injection would actually be dangerous.
            headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; "
                + "img-src 'self' data:; font-src 'self'; connect-src 'self'; "
                + "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

            // Stops a browser from guessing a content type it was not given, which is how a
            // JSON response gets executed as script.
            headers["X-Content-Type-Options"] = "nosniff";

            // No referrer at all: a panel URL names the server and often the customer's account,
            // and there is no third party this product needs to tell.
            headers["Referrer-Policy"] = "no-referrer";

            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), interest-cohort=()";

            await next();
        });

        return app;
    }
}
