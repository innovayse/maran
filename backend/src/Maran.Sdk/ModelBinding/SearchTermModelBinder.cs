using FluentValidation;
using FluentValidation.Results;
using Maran.Sdk.Resources;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Sdk.ModelBinding;

/// <summary>
/// Binds a list request's <c>search</c> parameter to a <see cref="SearchTerm"/>: absent, empty and
/// whitespace-only all bind to <see cref="SearchTerm.None"/>, and a term that is too long or carries a
/// control character is refused with <c>ListSearchTooLong</c> or <c>ListSearchInvalid</c>.
/// </summary>
/// <remarks>
/// A refusal is raised as the same <see cref="ValidationException"/> a list's validator raises, with
/// the code as both the failure's error code and its message, so it reaches the caller through the
/// one path every validation 400 takes — the panel's exception middleware — with the same problem
/// shape, the same <c>code</c> field and the same localized text. Reporting it through model state
/// instead would reduce it to <see cref="InvalidModelStateRefusal"/>'s generic
/// <c>RequestParameterInvalid</c>, losing the code that says what was wrong with the term.
/// </remarks>
public sealed class SearchTermModelBinder : IModelBinder
{
    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var text = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
        var verdict = SearchTerm.Parse(text, out var term);
        if (verdict != SearchTermVerdict.Accepted)
        {
            var code = verdict == SearchTermVerdict.TooLong
                ? nameof(ValidationMessages.ListSearchTooLong)
                : nameof(ValidationMessages.ListSearchInvalid);
            throw new ValidationException([new ValidationFailure(bindingContext.ModelName, code) { ErrorCode = code }]);
        }

        bindingContext.Result = ModelBindingResult.Success(term);
        return Task.CompletedTask;
    }
}
