using Maran.SharedKernel.Constants;

namespace Maran.Sdk.Extensions;

/// <summary>
/// Translates <see cref="Result{T}"/> into ASP.NET Core responses: success becomes 200/201, failure
/// becomes an RFC 7807 <see cref="ProblemDetails"/> carrying the machine-stable error code, a
/// localized message, and the request's correlation id — and nothing else. Never a stack trace, a
/// path, or tool output (rules/security.md "Secrets"). <see cref="Controllers.BaseApiController"/> is the usual
/// caller; the methods take <see cref="HttpContext"/> directly (rather than the controller) so they
/// stay unit-testable without standing up MVC.
/// </summary>
public static class ApiResultExtensions
{
    /// <summary>Translates a query/read result: 200 OK with the value, or a problem response.</summary>
    /// <param name="result">The outcome to translate.</param>
    /// <param name="httpContext">The current request, used to read the correlation id and resolve <see cref="IErrorTextProvider"/>.</param>
    public static IActionResult ToActionResult<T>(this Result<T> result, HttpContext httpContext)
    {
        return result.Match<IActionResult>(
            onOk: value =>
            {
                return new OkObjectResult(value);
            },
            onFail: error =>
            {
                return ToProblemResult(error, httpContext);
            });
    }

    /// <summary>Translates a create result: 201 Created with the value, or a problem response.</summary>
    /// <param name="result">The outcome to translate.</param>
    /// <param name="httpContext">The current request, used to read the correlation id and resolve <see cref="IErrorTextProvider"/>.</param>
    /// <param name="location">The URI of the created resource, used as the 201 response's <c>Location</c> header.</param>
    public static IActionResult ToCreatedActionResult<T>(this Result<T> result, HttpContext httpContext, string location)
    {
        return result.Match<IActionResult>(
            onOk: value =>
            {
                return new CreatedResult(location, value);
            },
            onFail: error =>
            {
                return ToProblemResult(error, httpContext);
            });
    }

    /// <summary>Builds the RFC 7807 problem response for a failed <see cref="Result{T}"/>.</summary>
    /// <param name="error">The typed domain failure.</param>
    /// <param name="httpContext">The current request.</param>
    private static ObjectResult ToProblemResult(Error error, HttpContext httpContext)
    {
        var correlationId = httpContext.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var item) ? item as string : null;

        // Resolved via DI rather than assumed present: a Host without any module loaded (or an
        // isolated unit test host) never registers Maran.SharedKernel.Localization.ResxErrorTextProvider,
        // and this must still degrade to the machine code rather than throw. Falling back to the code
        // is the same answer ResxErrorTextProvider gives for a key no module claims: machine-stable,
        // and never a path, a stack trace or tool output (rules/security.md "Secrets").
        var errorTextProvider = httpContext.RequestServices.GetService<IErrorTextProvider>();
        var detail = errorTextProvider?.Resolve(error.Code) ?? error.Code;

        var statusCode = MapStatusCode(error.Type);
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = detail,
            Detail = detail,
            Type = $"https://httpstatuses.io/{statusCode}",
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["correlationId"] = correlationId;

        return new ObjectResult(problem) { StatusCode = statusCode };
    }

    /// <summary>
    /// Maps a failure's <see cref="ErrorType"/> to its HTTP status. One arm per kind, no knowledge of
    /// any error code, and the only place in the panel where a status is chosen.
    /// </summary>
    /// <remarks>
    /// This method used to read the CODE and infer a status from its spelling, which is the design
    /// <see cref="ErrorType"/> exists to replace — see that type for the two ways it failed. Nothing
    /// here may grow a special case for a particular code: a failure that needs a different status
    /// needs a different <see cref="ErrorType"/>, declared where the error is built and visible to
    /// the handler's own tests.
    /// </remarks>
    /// <param name="type">The kind of failure, taken from the error itself.</param>
    private static int MapStatusCode(ErrorType type)
    {
        return type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            ErrorType.Failure => StatusCodes.Status500InternalServerError,

            // Unreachable while the enum and this switch agree, and deliberately NOT a 400: a kind
            // this method has never heard of is the panel failing to describe its own failure, which
            // is a server fault by definition. Answering 400 would blame the caller for a value they
            // could not have sent.
            _ => StatusCodes.Status500InternalServerError,
        };
    }
}
