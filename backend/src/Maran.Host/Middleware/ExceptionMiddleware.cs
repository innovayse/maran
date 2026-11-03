using FluentValidation;
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
    /// Machine-stable code for the generic fallback message, and the key of the Host's own
    /// <c>Resources/ErrorMessages*.resx</c> entry for it — one identifier, not a code plus a
    /// separate resource key that can drift apart (rules/csharp.md "That same string is the machine
    /// code"). <c>AddPanelLocalization</c> registers that resource family, so in the composed host
    /// this always resolves to a localized sentence.
    /// </summary>
    private const string UnexpectedErrorCode = "HostUnexpectedError";

    /// <summary>Code answered when a command failed its validator and the failure named no code of its own.</summary>
    private const string ValidationFailedCode = "HostValidationFailed";

    /// <summary>
    /// FluentValidation's own name for a minimum-length bound in a failure's placeholders; a validator
    /// that appends it makes the bound available to the text as <c>{1}</c>.
    /// </summary>
    private const string MinimumLengthPlaceholder = "MinLength";

    /// <summary>Media type of an RFC 7807 payload; must be handed to the JSON writer, not set beforehand.</summary>
    private const string ProblemContentType = "application/problem+json";

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
        catch (ValidationException validation)
        {
            // A command that failed its validator is the caller's mistake, not the server's: it
            // must answer 400 with the rule that was broken, never the anonymous 500 below. The
            // validator's message IS the error code when it names one (rules/csharp.md: the code
            // is the resource key), so the customer reads the rule in their own language.
            var correlationId = context.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var validationItem)
                ? validationItem as string
                : null;

            await WriteValidationProblemAsync(context, correlationId, validation);
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

    /// <summary>Writes the 400 problem response for a failed validator.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <param name="correlationId">The request's correlation id, or null when none was assigned.</param>
    /// <param name="validation">The failure raised by the command's validator.</param>
    private static async Task WriteValidationProblemAsync(
        HttpContext context,
        string? correlationId,
        ValidationException validation)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var (code, arguments) = ResolveValidationCode(validation);
        var errorTextProvider = context.RequestServices.GetService<IErrorTextProvider>();
        var detail = ResolveValidationDetail(errorTextProvider, code, arguments);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = detail,
            Detail = detail,
            Type = $"https://httpstatuses.io/{StatusCodes.Status400BadRequest}",
        };
        problem.Extensions["code"] = code;
        problem.Extensions[CorrelationIdKeys.PayloadField] = correlationId;

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: ProblemContentType);
    }

    /// <summary>
    /// Picks the error code for a validation failure — the first failure whose message is itself a
    /// code, or a generic one — together with the name of the field that failure is about.
    /// </summary>
    /// <remarks>
    /// FluentValidation's default messages are English sentences meant for developers ("'Password'
    /// must be at least 12 characters"), and shipping one to a customer would be an untranslated
    /// string the backend does not own (rules/csharp.md). A validator that wants a specific message
    /// says so with <c>.WithMessage("PasswordTooWeak")</c>; everything else collapses to the
    /// generic code, which at least is translated.
    /// <para>
    /// The text lookup receives the field name as <c>{0}</c>, so a sentence may name the value it
    /// refuses — <c>RequestParameterInvalid</c> does, because a value MVC could not bind is
    /// otherwise anonymous. The bound always follows as <c>{1}</c>: the <c>MinLength</c> the
    /// validator appended, so a sentence states the length the operator's policy requires rather
    /// than a number written into the resource (<c>PasswordTooWeak</c>), or an empty string when
    /// the failure carried none, so a text naming <c>{1}</c> is formatted instead of throwing. A
    /// sentence with no placeholder ignores both.
    /// </para>
    /// </remarks>
    /// <param name="validation">The failure raised by the command's validator.</param>
    /// <returns>A machine-stable error code that names a resource entry, and the text's format arguments.</returns>
    private static (string Code, object[] Arguments) ResolveValidationCode(ValidationException validation)
    {
        foreach (var failure in validation.Errors)
        {
            var message = failure.ErrorMessage;
            if (!string.IsNullOrEmpty(message) && message.All(char.IsLetterOrDigit))
            {
                var field = failure.PropertyName ?? string.Empty;
                object bound = string.Empty;
                if (failure.FormattedMessagePlaceholderValues is { } placeholders
                    && placeholders.TryGetValue(MinimumLengthPlaceholder, out var minimum))
                {
                    bound = minimum;
                }

                return (message, [field, bound]);
            }
        }

        return (ValidationFailedCode, [string.Empty]);
    }

    /// <summary>
    /// Resolves the sentence for a validation failure's code, and never throws while doing it: an
    /// answer to a bad request must not turn into a 500 because of how a resource text is written.
    /// </summary>
    /// <remarks>
    /// <see cref="ResolveValidationCode"/> supplies the field as <c>{0}</c> and the bound as
    /// <c>{1}</c> (empty when the failure carried none), so every text that names only those two
    /// formats. This guard is the last resort for a text that names a third argument nobody
    /// supplies, which the provider reports as a <see cref="FormatException"/>. The code stays what
    /// the caller receives, since it identifies the rule broken; the sentence falls back to the
    /// generic validation text, which takes no argument but the field, and then to the bare code.
    /// </remarks>
    /// <param name="errorTextProvider">The localizer, or null in a host that registers none.</param>
    /// <param name="code">The failure's machine-stable code.</param>
    /// <param name="arguments">The text's format arguments: the field, then the bound when there is one.</param>
    /// <returns>The localized sentence, the generic one, or the code itself.</returns>
    private static string ResolveValidationDetail(IErrorTextProvider? errorTextProvider, string code, object[] arguments)
    {
        if (errorTextProvider is null)
        {
            return code;
        }

        try
        {
            return errorTextProvider.Resolve(code, arguments);
        }
        catch (FormatException)
        {
            try
            {
                return errorTextProvider.Resolve(ValidationFailedCode, string.Empty);
            }
            catch (FormatException)
            {
                return code;
            }
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

        // Resolved via DI rather than assumed present, exactly as ApiResultExtensions.ToProblemResult
        // does: an isolated test host registers no IErrorTextProvider and must still degrade to the
        // machine code rather than throw. The code is never a stack trace, a path or tool output,
        // so it is safe to show (rules/security.md "Secrets"). There is deliberately no English
        // literal here any more: with the Host's own resource family registered, a literal would be
        // a hardcoded user-facing string that no code path in the composed host can reach.
        var errorTextProvider = context.RequestServices.GetService<IErrorTextProvider>();
        var detail = errorTextProvider?.Resolve(UnexpectedErrorCode) ?? UnexpectedErrorCode;

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
