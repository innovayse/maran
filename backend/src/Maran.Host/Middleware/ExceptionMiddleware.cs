using Maran.SharedKernel.Constants;
using Microsoft.AspNetCore.Mvc;

namespace Maran.Host.Middleware;

/// <summary>
/// Last-resort handler: anything that escapes a controller or endpoint becomes a 500 RFC 7807
/// <see cref="ProblemDetails"/> carrying the request's correlation id and a generic message —
/// never a stack trace, a path, or tool output (rules/security.md "Secrets"). The exception itself
/// is logged, with the correlation id, so the operator can find the real cause from the logs.
/// </summary>
public sealed class ExceptionMiddleware
{
    /// <summary>
    /// Machine-stable code for the generic fallback message. Not tied to any module, so it is
    /// resolved through <see cref="IErrorTextProvider"/> when one is registered (a future
    /// module may supply a localized "unexpected error" resource under this code); until then the
    /// English literal below is the only text that ever reaches a customer for this path, which
    /// keeps the "no hardcoded user-facing strings" rule honest — this is the single named
    /// exception, documented here rather than left implicit.
    /// </summary>
    private const string UnexpectedErrorCode = "HostUnexpectedError";

    /// <summary>Media type of an RFC 7807 payload; must be handed to the JSON writer, not set beforehand.</summary>
    private const string ProblemContentType = "application/problem+json";

    /// <summary>The literal used when no <see cref="IErrorTextProvider"/> is registered to localize it.</summary>
    private const string UnexpectedErrorFallbackText = "An unexpected error occurred.";

    /// <summary>
    /// Pre-compiled log delegate for the caught exception. A source-generated delegate avoids the
    /// boxing and format parsing of a direct <c>LogError</c> call on a path that, under an attack
    /// or an outage, can be hit on every request.
    /// </summary>
    private static readonly Action<ILogger, string?, Exception?> LogUnhandledException =
        LoggerMessage.Define<string?>(
            LogLevel.Error,
            new EventId(1, nameof(ExceptionMiddleware)),
            "Unhandled exception for correlation id {CorrelationId}");

    /// <summary>The next component in the pipeline.</summary>
    private readonly RequestDelegate _next;

    /// <summary>Logger the caught exception is recorded to.</summary>
    private readonly ILogger<ExceptionMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    /// <param name="logger">Logger the caught exception is recorded to.</param>
    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Runs the rest of the pipeline, converting any escaping exception into a 500 problem response.</summary>
    /// <param name="context">The current HTTP request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var item)
                ? item as string
                : null;

            LogUnhandledException(_logger, correlationId, ex);

            await WriteProblemAsync(context, correlationId);
        }
    }

    /// <summary>Writes the generic 500 problem response. Never includes exception detail.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <param name="correlationId">The request's correlation id, or null when none was assigned.</param>
    private static async Task WriteProblemAsync(HttpContext context, string? correlationId)
    {
        if (context.Response.HasStarted)
        {
            // The response body was already partially written before the exception occurred;
            // rewriting it would corrupt the stream, so there is nothing safe left to do.
            return;
        }

        var errorTextProvider = context.RequestServices.GetService<IErrorTextProvider>();
        var detail = errorTextProvider?.Resolve(UnexpectedErrorCode) ?? UnexpectedErrorFallbackText;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = detail,
            Detail = detail,
            Type = $"https://httpstatuses.io/{StatusCodes.Status500InternalServerError}",
        };
        problem.Extensions["code"] = UnexpectedErrorCode;
        problem.Extensions[CorrelationIdKeys.PayloadField] = correlationId;

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        // The content type must be passed to the writer: setting Response.ContentType beforehand
        // is overwritten by WriteAsJsonAsync, which would ship an RFC 7807 body labelled
        // application/json and break clients that dispatch on the media type.
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: ProblemContentType);
    }
}
