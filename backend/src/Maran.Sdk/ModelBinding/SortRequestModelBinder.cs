using FluentValidation;
using FluentValidation.Results;
using Maran.Sdk.Resources;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Sdk.ModelBinding;

/// <summary>
/// Binds a list request's <c>sort</c> parameter to a <see cref="SortRequest"/>: absent or empty binds
/// to <see cref="SortRequest.Default"/>, and text that is not a well-formed key (<c>name</c>,
/// <c>-createdAt</c>) is refused with <c>ListSortUnknown</c>. Whether a well-formed key names a field
/// the list exposes is decided later by the list's validator, which holds the list's sort map.
/// </summary>
/// <remarks>
/// A refusal travels the same path as <see cref="SearchTermModelBinder"/>'s, for the reason given there.
/// </remarks>
public sealed class SortRequestModelBinder : IModelBinder
{
    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var text = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
        if (!SortRequest.TryParse(text, out var request))
        {
            const string code = nameof(ValidationMessages.ListSortUnknown);
            throw new ValidationException([new ValidationFailure(bindingContext.ModelName, code) { ErrorCode = code }]);
        }

        bindingContext.Result = ModelBindingResult.Success(request);
        return Task.CompletedTask;
    }
}
