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
    public static IActionResult ToActionResult<T>(this Result<T> result, HttpContext httpContext) =>
        result.Match<IActionResult>(
            onOk: value => new OkObjectResult(value),
            onFail: error => ToProblemResult(error, httpContext));

    /// <summary>Translates a create result: 201 Created with the value, or a problem response.</summary>
    /// <param name="result">The outcome to translate.</param>
    /// <param name="httpContext">The current request, used to read the correlation id and resolve <see cref="IErrorTextProvider"/>.</param>
    /// <param name="location">The URI of the created resource, used as the 201 response's <c>Location</c> header.</param>
    public static IActionResult ToCreatedActionResult<T>(this Result<T> result, HttpContext httpContext, string location) =>
        result.Match<IActionResult>(
            onOk: value => new CreatedResult(location, value),
            onFail: error => ToProblemResult(error, httpContext));

    /// <summary>Builds the RFC 7807 problem response for a failed <see cref="Result{T}"/>.</summary>
    /// <param name="error">The typed domain failure.</param>
    /// <param name="httpContext">The current request.</param>
    private static ObjectResult ToProblemResult(Error error, HttpContext httpContext)
    {
        var correlationId = httpContext.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var item) ? item as string : null;

        // Resolved via DI rather than assumed present: a Host without any module loaded (or an
        // isolated unit test host) never registers Maran.SharedKernel.Localization.ResxErrorTextProvider,
        // and this must still degrade to the machine code rather than throw. The machine code is
        // never Error.Message, which is documented operator-only text that must not reach customers.
        var errorTextProvider = httpContext.RequestServices.GetService<IErrorTextProvider>();
        var detail = errorTextProvider?.Resolve(error.Code) ?? error.Code;

        var statusCode = MapStatusCode(error.Code);
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
    /// Infers an HTTP status from the machine error code's suffix convention (e.g.
    /// <c>"sites.not_found"</c> → 404). Modules are free to use any suffix; unrecognized ones map
    /// to 400, so a new code never silently produces a wrong-but-plausible status.
    /// </summary>
    /// <param name="code">The machine-stable error code.</param>
    private static int MapStatusCode(string code) => code switch
    {
        _ when code.EndsWith(".not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ when code.EndsWith(".already_exists", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
        _ when code.EndsWith(".taken", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
        _ when code.EndsWith(".forbidden", StringComparison.Ordinal) => StatusCodes.Status403Forbidden,
        _ when code.EndsWith(".unauthorized", StringComparison.Ordinal) => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status400BadRequest,
    };
}
