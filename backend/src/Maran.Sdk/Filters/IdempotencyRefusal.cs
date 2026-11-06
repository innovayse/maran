using Maran.SharedKernel.Constants;

namespace Maran.Sdk.Filters;

/// <summary>
/// Builds the idempotency filter's refusals in the panel's one problem shape: RFC 7807, the machine
/// <c>code</c>, the <c>correlationId</c>, and a title in the caller's language.
/// </summary>
/// <remarks>
/// This repeats the shape <c>ApiResultExtensions</c> writes because that class derives a status from
/// an <see cref="ErrorType"/> and has no kind for 422 (a reused key), and adding a kind for one
/// filter would ripple through the status census; the shape is three lines and is pinned by this
/// filter's tests.
/// </remarks>
internal static class IdempotencyRefusal
{
    /// <summary>Builds the problem response for a code and a status.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="code">The machine code, one of <see cref="Contracts.IdempotencyErrorCodes"/>.</param>
    /// <param name="statusCode">The HTTP status.</param>
    /// <returns>The response, as an <see cref="ObjectResult"/> carrying <see cref="ProblemDetails"/>.</returns>
    public static ObjectResult Of(HttpContext httpContext, string code, int statusCode)
    {
        var correlationId = httpContext.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var item) ? item as string : null;
        var text = httpContext.RequestServices.GetService<IErrorTextProvider>()?.Resolve(code) ?? code;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = text,
            Detail = text,
            Type = $"https://httpstatuses.io/{statusCode}",
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = correlationId;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
