using Maran.Host.Demo.Common;
using Maran.Host.Demo.Guard.Enums;
using Maran.Host.Demo.Resources;
using Maran.Host.RateLimiting;
using Maran.Sdk.Extensions;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.Extensions.Localization;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// Writes the guard's three refusals in the panel's own problem shapes. The 429 is the Host's one
/// <see cref="RateLimitedProblemWriter"/>, so the demonstration cannot drift from the product's code, media
/// type or body.
/// </summary>
public static class DemoProblemWriter
{
    /// <summary>
    /// Code and resource key of a refused action (status 403); its area rides beside it as <c>refusal</c>.
    /// </summary>
    public const string RefusedCode = "DemoActionRefused";

    /// <summary>Code and resource key of text the content filter refuses (status 400).</summary>
    public const string ContentRefusedCode = "DemoContentRefused";

    /// <summary>Code and resource key of a spent budget (status 409).</summary>
    public const string BudgetExhaustedCode = "DemoBudgetExhausted";

    /// <summary>Writes the 403 for a refused action, with the area's sentence in the caller's language.</summary>
    /// <param name="context">The current request.</param>
    /// <param name="area">The area that explains the refusal.</param>
    /// <returns>Resolves once the body is written.</returns>
    public static async Task WriteRefusedAsync(HttpContext context, DemoRefusalArea area)
    {
        var id = DemoRefusalAreaIds.IdOf(area);
        var messages = context.RequestServices.GetService<IStringLocalizer<DemoMessages>>();
        var refusal = new DemoRefusalDto(id, messages?[id].Value ?? id);

        var result = Result<bool>.Fail(
            Error.Of(RefusedCode, ErrorType.Forbidden),
            ProblemExtension.Of("refusal", refusal));
        await ExecuteAsync(context, result.ToActionResult(context));
    }

    /// <summary>Writes the 400 for refused text, naming the member and never echoing the text.</summary>
    /// <param name="context">The current request.</param>
    /// <param name="field">The member that held the text; a name from the code, not a visitor's value.</param>
    /// <returns>Resolves once the body is written.</returns>
    public static async Task WriteContentRefusedAsync(HttpContext context, string field)
    {
        var result = Result<bool>.Fail(
            Error.Of(ContentRefusedCode, ErrorType.Validation),
            ProblemExtension.Of("content", new DemoContentRefusalDto(field)));
        await ExecuteAsync(context, result.ToActionResult(context));
    }

    /// <summary>Writes the 409 for a spent budget.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>Resolves once the body is written.</returns>
    public static async Task WriteBudgetExhaustedAsync(HttpContext context)
    {
        var result = Result<bool>.Fail(Error.Of(BudgetExhaustedCode, ErrorType.Conflict));
        await ExecuteAsync(context, result.ToActionResult(context));
    }

    /// <summary>Writes the 429 for a visitor over their own rate, with <c>Retry-After</c>.</summary>
    /// <param name="context">The current request.</param>
    /// <param name="retryAfter">How long until the visitor's window ends.</param>
    /// <returns>Resolves once the body is written.</returns>
    public static async Task WriteRateLimitedAsync(HttpContext context, TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        await RateLimitedProblemWriter.WriteAsync(context, seconds, context.RequestAborted);
    }

    /// <summary>Executes a controller-style result outside a controller.</summary>
    /// <param name="context">The current request.</param>
    /// <param name="result">The result to write.</param>
    /// <returns>Resolves once the body is written.</returns>
    private static async Task ExecuteAsync(HttpContext context, IActionResult result)
    {
        await result.ExecuteResultAsync(new ActionContext(context, context.GetRouteData(), new ActionDescriptor()));
    }
}
