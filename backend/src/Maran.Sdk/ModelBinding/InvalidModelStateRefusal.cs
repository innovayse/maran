using FluentValidation;
using FluentValidation.Results;
using Maran.Sdk.Resources;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Sdk.ModelBinding;

/// <summary>
/// The panel's answer to a request value MVC could not bind — <c>?page=abc</c>, <c>?pageSize=x</c>,
/// <c>?status=7</c> for an enum that has no member 7, a route value of the wrong type: it is refused
/// with <c>RequestParameterInvalid</c>, naming the value that failed, before any validator runs.
/// Installed as <see cref="ApiBehaviorOptions.InvalidModelStateResponseFactory"/> by the composition
/// root, so it covers every <c>[FromQuery]</c>, <c>[FromRoute]</c> and <c>[FromBody]</c> parameter of
/// every module.
/// </summary>
/// <remarks>
/// <para>
/// It is the FALLBACK, not the first word. A binder that knows its parameter refuses it with a code
/// of its own — <see cref="SearchTermModelBinder"/> and <see cref="SortRequestModelBinder"/> do — and
/// that refusal is raised while binding, so this factory never sees it. What reaches here is only
/// what MVC's own binders refused, and those answer with an English developer sentence in model
/// state that no customer may be shown.
/// </para>
/// <para>
/// The refusal is raised as the same <see cref="ValidationException"/> the paging binders and every
/// validator raise, so it reaches the caller through the one 400 writer the panel has — the Host's
/// exception middleware — with the same problem shape, the same <c>code</c> field and text localized
/// in the request's language. Returning a result from here instead would be a second writer of that
/// shape, which is the thing MVC's default factory was: a 400 with no <c>code</c>.
/// </para>
/// </remarks>
public static class InvalidModelStateRefusal
{
    /// <summary>
    /// Refuses the request, naming the first model-state entry that failed to bind. Never returns:
    /// the signature is the one <see cref="ApiBehaviorOptions.InvalidModelStateResponseFactory"/> asks for.
    /// </summary>
    /// <param name="context">The action whose model state is invalid.</param>
    /// <returns>Nothing; the method always throws.</returns>
    /// <exception cref="ValidationException">Always, carrying <c>RequestParameterInvalid</c>.</exception>
    public static IActionResult Refuse(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var parameter = context.ModelState
            .Where(entry =>
            {
                return entry.Value?.ValidationState == ModelValidationState.Invalid;
            })
            .Select(entry =>
            {
                return entry.Key;
            })
            .FirstOrDefault() ?? string.Empty;

        const string code = nameof(ValidationMessages.RequestParameterInvalid);
        throw new ValidationException([new ValidationFailure(parameter, code) { ErrorCode = code }]);
    }
}
