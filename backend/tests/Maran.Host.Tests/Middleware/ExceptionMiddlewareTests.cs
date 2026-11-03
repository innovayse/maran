using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Maran.Host.Middleware;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Interfaces;
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

    /// <summary>A validation failure whose text wants the bound as {1} gets it when the validator appended one.</summary>
    [Fact]
    public async Task A_validation_failure_with_a_min_length_placeholder_formats_the_bound_into_the_text()
    {
        var failure = new ValidationFailure("NewPassword", "PasswordTooWeak")
        {
            FormattedMessagePlaceholderValues = new Dictionary<string, object> { ["MinLength"] = 16 },
        };

        var (status, code, title) = await InvokeWithValidationFailureAsync(failure);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("PasswordTooWeak", code);
        Assert.Equal("at least 16 characters", title);
    }

    /// <summary>A text asking for {1} when the failure carried no bound still formats and never throws.</summary>
    /// <remarks>
    /// Without a default for the bound the provider's <see cref="FormatException"/> escaped the
    /// validation handler and the caller's 400 became a 500. The code is kept: it names the rule broken.
    /// </remarks>
    [Fact]
    public async Task A_text_wanting_a_bound_the_failure_did_not_carry_formats_with_an_empty_bound()
    {
        var failure = new ValidationFailure("NewPassword", "PasswordTooWeak");

        var (status, code, title) = await InvokeWithValidationFailureAsync(failure);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("PasswordTooWeak", code);
        Assert.Equal("at least  characters", title);
    }

    /// <summary>A text naming an argument nobody supplies degrades to the generic sentence and never throws.</summary>
    /// <remarks>The last-resort guard: the code is kept, the sentence becomes the generic validation text.</remarks>
    [Fact]
    public async Task A_text_naming_an_argument_nobody_supplies_degrades_to_the_generic_sentence()
    {
        var failure = new ValidationFailure("Name", "NeedsAThirdArgument");

        var (status, code, title) = await InvokeWithValidationFailureAsync(failure);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("NeedsAThirdArgument", code);
        Assert.Equal("Some details are not valid.", title);
    }

    /// <summary>Runs the middleware over a validator failure and reads back what it answered.</summary>
    /// <param name="failure">The failure the downstream component raises.</param>
    private static async Task<(int Status, string Code, string Title)> InvokeWithValidationFailureAsync(
        ValidationFailure failure)
    {
        var context = CreateContext(correlationId: "abc-123");
        context.RequestServices = new ServiceCollection()
            .AddSingleton<IErrorTextProvider>(new FormattingErrorTextProvider())
            .BuildServiceProvider();
        var middleware = new ExceptionMiddleware(
            _ =>
            {
                throw new ValidationException(new[] { failure });
            },
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        using var body = await ReadResponseBodyAsync(context);
        return (
            context.Response.StatusCode,
            body.RootElement.GetProperty("code").GetString()!,
            body.RootElement.GetProperty("title").GetString()!);
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

    /// <summary>
    /// An <see cref="IErrorTextProvider"/> that formats a fixed table the way the real one does —
    /// <see cref="string.Format(IFormatProvider, string, object[])"/> over the caller's arguments — so
    /// a text naming an argument the caller did not supply throws exactly as it would in production.
    /// </summary>
    private sealed class FormattingErrorTextProvider : IErrorTextProvider
    {
        /// <summary>The texts, keyed by code; {0} is the field and {1} the optional bound.</summary>
        private static readonly Dictionary<string, string> Texts = new()
        {
            ["PasswordTooWeak"] = "at least {1} characters",
            ["NeedsAThirdArgument"] = "{2}",
            ["HostValidationFailed"] = "Some details are not valid.",
        };

        /// <inheritdoc/>
        public string Resolve(string code, params object[] arguments)
        {
            return string.Format(CultureInfo.InvariantCulture, Texts[code], arguments);
        }

        /// <inheritdoc/>
        public bool TryResolve(string code, [NotNullWhen(true)] out string? text)
        {
            return Texts.TryGetValue(code, out text);
        }
    }
}
