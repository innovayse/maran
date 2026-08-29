using Maran.SharedKernel.Constants;

namespace Maran.Host.Middleware;

/// <summary>
/// Assigns every request a correlation id: reuses an incoming <see cref="CorrelationIdKeys.HeaderName"/>
/// header when the caller sent one, otherwise mints a new one. The id is stored under
/// <see cref="CorrelationIdKeys.ItemsKey"/> (read back by <see cref="CorrelationIdAccessor"/> and by
/// <c>ApiResultExtensions</c>), echoed on the response so the caller can correlate it, and pushed
/// into the logging scope so every log line for this request carries it.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    /// <summary>The next component in the pipeline.</summary>
    private readonly RequestDelegate _next;

    /// <summary>Logger used to push the correlation id into the structured logging scope.</summary>
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    /// <param name="logger">Logger used to push the correlation id into the structured logging scope.</param>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Resolves or mints the correlation id, stores it, echoes it, and invokes the rest of the pipeline.</summary>
    /// <param name="context">The current HTTP request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[CorrelationIdKeys.ItemsKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdKeys.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object> { [CorrelationIdKeys.PayloadField] = correlationId }))
        {
            await _next(context);
        }
    }

    /// <summary>Reads the incoming header when present and non-empty, otherwise mints a new id.</summary>
    /// <param name="context">The current HTTP request.</param>
    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdKeys.HeaderName, out var values))
        {
            var incoming = values.ToString();
            if (!string.IsNullOrWhiteSpace(incoming))
            {
                return incoming;
            }
        }

        return Guid.NewGuid().ToString();
    }
}
