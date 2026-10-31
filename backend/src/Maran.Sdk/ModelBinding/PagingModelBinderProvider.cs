using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Sdk.ModelBinding;

/// <summary>
/// Supplies the binders for the list contract's typed parameters — <see cref="SearchTermModelBinder"/>
/// for a <see cref="SearchTerm"/>, <see cref="SortRequestModelBinder"/> for a
/// <see cref="SortRequest"/> and <see cref="FilterValueModelBinder"/> for a <see cref="FilterValue"/> —
/// and nothing for any other type. Registered once, by the composition
/// root, ahead of MVC's own providers, so every module's list query binds the same way.
/// </summary>
public sealed class PagingModelBinderProvider : IModelBinderProvider
{
    /// <summary>The one search binder; it holds no state.</summary>
    private static readonly SearchTermModelBinder SearchBinder = new();

    /// <summary>The one sort binder; it holds no state.</summary>
    private static readonly SortRequestModelBinder SortBinder = new();

    /// <summary>The one optional-filter binder; it holds no state.</summary>
    private static readonly FilterValueModelBinder FilterBinder = new();

    /// <inheritdoc />
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Metadata.ModelType == typeof(SearchTerm))
        {
            return SearchBinder;
        }

        if (context.Metadata.ModelType == typeof(SortRequest))
        {
            return SortBinder;
        }

        if (context.Metadata.ModelType == typeof(FilterValue))
        {
            return FilterBinder;
        }

        return null;
    }
}
