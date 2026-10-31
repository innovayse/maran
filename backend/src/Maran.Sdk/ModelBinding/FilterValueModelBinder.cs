using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Sdk.ModelBinding;

/// <summary>
/// Binds a list request's optional text filter (<c>kind</c>, <c>action</c>, <c>phpVersion</c>) to a
/// <see cref="FilterValue"/>: absent and empty bind to <see cref="FilterValue.None"/>, anything else to
/// the value as written. It refuses nothing — what text a filter may hold is the module's validator's
/// to decide, through the one validation 400 every list refusal takes.
/// </summary>
public sealed class FilterValueModelBinder : IModelBinder
{
    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var text = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
        bindingContext.Result = ModelBindingResult.Success(FilterValue.From(text));
        return Task.CompletedTask;
    }
}
