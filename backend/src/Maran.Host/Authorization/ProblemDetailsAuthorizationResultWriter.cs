using Maran.Modules.Identity.Authorization;
using Maran.SharedKernel.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace Maran.Host.Authorization;

/// <summary>
/// Writes a policy refusal (403) as a localized RFC 7807 body instead of the framework's empty one,
/// and hands every other authorization result to the framework's default handler unchanged.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the body changes, never the decision.</b> Which requests are refused is still decided by
/// the policies in <see cref="RolePolicies"/>; a challenge (401) and a success still go through
/// <see cref="AuthorizationMiddlewareResultHandler"/>.
/// </para>
/// <para>
/// <b>The text is generic on purpose.</b> The fallback policy also refuses a signed-in customer on a
/// route that matched nothing, so the same sentence must describe a real administrator route and a
/// route that does not exist. Naming a role or a route would let a customer tell the two apart.
/// </para>
/// <para>
/// <b>Two-factor steering keeps its own code.</b> When
/// <see cref="TwoFactorEnrolmentCompleteRequirement"/> is among the failed requirements the code is
/// <see cref="TwoFactorSetupRequiredCode"/>, even if another requirement failed too, so the SPA
/// still sends the person to enrolment first. It discloses only what the caller's own token says.
/// The threat argument is in docs/superpowers/notes/2026-10-30-forbidden-problem-details-threat-note.md.
/// </para>
/// </remarks>
public sealed class ProblemDetailsAuthorizationResultWriter : IAuthorizationMiddlewareResultHandler
{
    /// <summary>Code and resource key of a generic policy refusal.</summary>
    public const string ForbiddenCode = "HostForbidden";

    /// <summary>Code and resource key of a refusal caused by an unfinished forced two-factor enrolment.</summary>
    public const string TwoFactorSetupRequiredCode = "HostTwoFactorSetupRequired";

    /// <summary>Media type of an RFC 7807 payload; must be handed to the JSON writer, not set beforehand.</summary>
    private const string ProblemContentType = "application/problem+json";

    /// <summary>The framework's handler, which every non-forbidden result is delegated to.</summary>
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    /// <summary>Delegates to the default handler unless the result is a policy refusal.</summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="context">The current HTTP request.</param>
    /// <param name="policy">The policy that was evaluated.</param>
    /// <param name="authorizeResult">The outcome of evaluating it.</param>
    /// <returns>Resolves once the response is written or the pipeline has continued.</returns>
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            await _fallback.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        await WriteForbiddenAsync(context, ResolveCode(authorizeResult.AuthorizationFailure));
    }

    /// <summary>Picks the code for a refusal from the requirements that failed.</summary>
    /// <param name="failure">What failed, when the framework reports it.</param>
    /// <returns><see cref="TwoFactorSetupRequiredCode"/> when enrolment is unfinished, otherwise <see cref="ForbiddenCode"/>.</returns>
    private static string ResolveCode(AuthorizationFailure? failure)
    {
        var steered = failure?.FailedRequirements.OfType<TwoFactorEnrolmentCompleteRequirement>().Any() == true;
        return steered ? TwoFactorSetupRequiredCode : ForbiddenCode;
    }

    /// <summary>Writes the 403 problem body in the request culture.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <param name="code">The machine code, which is also the resource key.</param>
    /// <returns>Resolves once the body is written.</returns>
    private static async Task WriteForbiddenAsync(HttpContext context, string code)
    {
        // Resolved from DI rather than assumed, as ExceptionMiddleware does: a host without the
        // provider degrades to the machine code instead of throwing.
        var errorTextProvider = context.RequestServices.GetService<IErrorTextProvider>();
        var text = errorTextProvider?.Resolve(code) ?? code;
        var correlationId = context.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var item)
            ? item as string
            : null;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = text,
            Detail = text,
            Type = $"https://httpstatuses.io/{StatusCodes.Status403Forbidden}",
        };
        problem.Extensions["code"] = code;
        problem.Extensions[CorrelationIdKeys.PayloadField] = correlationId;

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: ProblemContentType);
    }
}
