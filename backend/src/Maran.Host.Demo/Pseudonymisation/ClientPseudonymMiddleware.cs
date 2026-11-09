namespace Maran.Host.Demo.Pseudonymisation;

/// <summary>
/// Replaces the connection's remote address with its stand-in and the <c>User-Agent</c> with its family,
/// and removes every header that still carries the real address, so nothing after it can store, count,
/// ban or log a visitor's real address (threat note T9).
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it sits.</b> Immediately AFTER forwarded-header handling, which is what turns the proxy's
/// <c>X-Forwarded-For</c> into the connection's remote address, and BEFORE request logging, correlation,
/// rate limiting, authentication and the guard. An address something logs before this runs is not
/// protected; that placement is part of what a reviewer checks.
/// </para>
/// <para>
/// A connection with no address (an in-memory test host) is left without one: there is nothing real to
/// hide, and inventing a stand-in would put every such request in one bucket.
/// </para>
/// </remarks>
public sealed class ClientPseudonymMiddleware
{
    /// <summary>The headers that carry the real address after forwarded-header handling.</summary>
    public static readonly string[] AddressHeaders =
    [
        "X-Forwarded-For",
        "X-Original-For",
        "X-Real-IP",
        "Forwarded",
        "CF-Connecting-IP",
        "True-Client-IP",
        "X-Client-IP",
        "X-Cluster-Client-IP",
        "Fastly-Client-IP",
    ];

    /// <summary>The next component in the pipeline.</summary>
    private readonly RequestDelegate _next;

    /// <summary>The address stand-ins.</summary>
    private readonly ClientPseudonymiser _pseudonymiser;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    /// <param name="pseudonymiser">The address stand-ins.</param>
    public ClientPseudonymMiddleware(RequestDelegate next, ClientPseudonymiser pseudonymiser)
    {
        _next = next;
        _pseudonymiser = pseudonymiser;
    }

    /// <summary>Rewrites the request's identity, then hands it on.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>Resolves once the rest of the pipeline has run.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var real = context.Connection.RemoteIpAddress;
        if (real is not null)
        {
            context.Connection.RemoteIpAddress = _pseudonymiser.Of(real);
        }

        foreach (var header in AddressHeaders)
        {
            context.Request.Headers.Remove(header);
        }

        context.Request.Headers.UserAgent = UserAgentFamilies.Of(context.Request.Headers.UserAgent.ToString());

        await _next(context);
    }
}
