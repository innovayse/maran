using System.Globalization;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Writes the panel's one rate-limited answer: a 429 with a <c>Retry-After</c> header and an RFC 7807
/// body carrying the <c>HostRateLimited</c> code and its sentence in the request culture. The
/// framework limiter's rejection handler and the dashboard stream's process-wide slot gate both end
/// here, so the two refusals cannot drift apart in code, media type or body shape.
/// </summary>
public static class RateLimitedProblemWriter
{
    /// <summary>
    /// Machine-stable code of the answer, and the key of its <c>Resources/ErrorMessages*.resx</c> entry:
    /// one identifier, not a code plus a separate resource key that can drift apart
    /// (rules/csharp.md "That same string is the machine code").
    /// </summary>
    public const string Code = "HostRateLimited";

    /// <summary>The media type of the panel's problem answers (RFC 7807).</summary>
    private const string ProblemContentType = "application/problem+json";

    /// <summary>Writes the 429 answer onto the response.</summary>
    /// <param name="httpContext">The refused request; its services resolve the sentence in its culture.</param>
    /// <param name="retryAfterSeconds">Seconds the caller is told to wait, sent as <c>Retry-After</c>.</param>
    /// <param name="cancellationToken">Cancels the write when the caller goes away.</param>
    /// <returns>Resolves once the body is written.</returns>
    public static async Task WriteAsync(HttpContext httpContext, int retryAfterSeconds, CancellationToken cancellationToken)
    {
        // A rejected caller must learn when to come back; an empty 429 forces clients to guess, and
        // guessing means retry storms.
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        // The media type is passed to the writer rather than set on the response first:
        // WriteAsJsonAsync without a contentType overwrites it with application/json, which is how
        // this answer once carried the problem fields under the wrong media type (issue #260).
        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                status = StatusCodes.Status429TooManyRequests,
                title = ResolveText(httpContext),
                code = Code,
            },
            options: null,
            contentType: ProblemContentType,
            cancellationToken);
    }

    /// <summary>
    /// Resolves the sentence in the request's culture, the same way <c>ApiResultExtensions.ToProblemResult</c>
    /// does: the provider is looked up from the request services rather than assumed present, so a host
    /// built without the SharedKernel registration (an isolated test host) degrades to the machine code
    /// instead of throwing. The code is never a stack trace, a path or tool output, so it is safe to show
    /// (rules/security.md "Secrets").
    /// </summary>
    /// <param name="httpContext">The refused request, whose services and culture are used.</param>
    /// <returns>The localized sentence, or <see cref="Code"/> when nothing resolves it.</returns>
    private static string ResolveText(HttpContext httpContext)
    {
        var errorTextProvider = httpContext.RequestServices.GetService<IErrorTextProvider>();
        return errorTextProvider?.Resolve(Code) ?? Code;
    }
}
