using System.Text.Json;
using Maran.Host.Middleware;
using Maran.SharedKernel.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.Tests.Middleware;

/// <summary>
/// Behavioral contract of <see cref="Host.Middleware.ExceptionMiddleware"/>. Exercised as a plain
/// unit around a fake <see cref="HttpContext"/> rather than through the full host, since the
/// behavior under test is entirely within the middleware itself.
/// </summary>
public sealed class ExceptionMiddlewareTests
{
    /// <summary>Unhandled exception becomes a 500 problem response with the correlation id.</summary>
    [Fact]
    public async Task Unhandled_exception_becomes_a_500_problem_response_with_the_correlation_id()
    {
        var context = CreateContext(correlationId: "abc-123");
        var middleware = new ExceptionMiddleware(
            _ =>
            {
                throw new InvalidOperationException("some internal detail");
            },
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        using var body = await ReadResponseBodyAsync(context);
        Assert.Equal("abc-123", body.RootElement.GetProperty(CorrelationIdKeys.PayloadField).GetString());
        Assert.Equal("HostUnexpectedError", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>Unhandled exception becomes problem response without stack trace or exception text.</summary>
    [Fact]
    public async Task Unhandled_exception_becomes_problem_response_without_stack_trace_or_exception_text()
    {
        var context = CreateContext(correlationId: "abc-123");
        var middleware = new ExceptionMiddleware(
            _ =>
            {
                throw new InvalidOperationException("SECRET_DETAIL: /etc/maran/panel.env leaked");
            },
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        using var body = await ReadResponseBodyAsync(context);
        var raw = body.RootElement.GetRawText();

        Assert.DoesNotContain("SECRET_DETAIL", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("panel.env", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs:line", raw, StringComparison.Ordinal);
    }

    /// <summary>Response already started is left untouched.</summary>
    [Fact]
    public async Task Response_already_started_is_left_untouched()
    {
        var context = CreateContext(correlationId: "abc-123");
        // Simulates a response that already began sending headers/body before the failure:
        // the middleware must not attempt to clear and rewrite it.
        context.Features.Set<IHttpResponseFeature>(new AlreadyStartedResponseFeature());
        context.Response.StatusCode = StatusCodes.Status206PartialContent;

        var middleware = new ExceptionMiddleware(
            _ =>
            {
                throw new InvalidOperationException("boom");
            },
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status206PartialContent, context.Response.StatusCode);
    }

    /// <summary>Builds a fake request context with a resolvable, correlation-id-bearing state.</summary>
    /// <param name="correlationId">The correlation id to seed <see cref="CorrelationIdKeys.ItemsKey"/> with.</param>
    private static DefaultHttpContext CreateContext(string correlationId)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
        context.Items[CorrelationIdKeys.ItemsKey] = correlationId;
        return context;
    }

    /// <summary>Rewinds and parses the response body written by the middleware.</summary>
    /// <param name="context">The context the middleware wrote its response into.</param>
    private static async Task<JsonDocument> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        return JsonDocument.Parse(text);
    }

    /// <summary>A minimal <see cref="IHttpResponseFeature"/> stub reporting <see cref="HasStarted"/> as true.</summary>
    private sealed class AlreadyStartedResponseFeature : IHttpResponseFeature
    {
        /// <inheritdoc/>
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        /// <inheritdoc/>
        public string? ReasonPhrase { get; set; }

        /// <inheritdoc/>
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        /// <inheritdoc/>
        public Stream Body { get; set; } = new MemoryStream();

        /// <inheritdoc/>
        public bool HasStarted
        {
            get
            {
                return true;
            }
        }

        /// <inheritdoc/>
        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        /// <inheritdoc/>
        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }
}
