namespace Maran.Host.Middleware;

/// <summary>
/// Refuses a state-changing request that carries a panel cookie but not the panel's own header
/// (spec §10: CSRF is <c>SameSite</c> plus a mandatory custom header).
/// </summary>
/// <remarks>
/// Cross-site request forgery exists because a browser attaches cookies to a request the page did
/// not intend to make. A custom header cannot be set by a plain cross-site form or an image tag, and
/// a cross-origin <c>fetch</c> that tries has to pass a preflight this server never answers — so
/// requiring the header turns "the browser sent the cookie for you" back into "the page meant it".
///
/// It applies only to a request that actually carries a cookie. One authenticated purely by an
/// <c>Authorization</c> header cannot be forged cross-site at all — nothing attaches that header
/// automatically — so demanding the header there would break API clients to prevent nothing.
/// </remarks>
public sealed class CsrfHeaderMiddleware
{
    /// <summary>The header the SPA sets on every request it makes.</summary>
    public const string HeaderName = "X-Maran-Request";

    /// <summary>Methods that change state and therefore need the header.</summary>
    private static readonly string[] UnsafeMethods = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>The next component in the pipeline.</summary>
    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    public CsrfHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Rejects a cookie-bearing state change that did not come from the panel.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>Resolves once the request has been handled or refused.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresHeader(context) && !context.Request.Headers.ContainsKey(HeaderName))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }

    /// <summary>Decides whether this request must carry the header.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>True for a state-changing request that carries at least one cookie.</returns>
    private static bool RequiresHeader(HttpContext context)
    {
        return UnsafeMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase)
            && context.Request.Cookies.Count > 0;
    }
}
