using Maran.Sdk.ModelBinding;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the list contract's model binders with MVC, once for the whole panel, so every module's
/// list query binds <c>search</c> and <c>sort</c> through the same rules, and installs
/// <see cref="InvalidModelStateRefusal"/> as the answer to any value MVC's own binders refuse
/// (rules/csharp.md, the <c>Maran.Sdk/ModelBinding/</c> map entry).
/// </summary>
public static class ModelBindingExtensions
{
    /// <summary>
    /// Puts <see cref="PagingModelBinderProvider"/> ahead of MVC's own binder providers, tells
    /// MVC's validation not to walk into the two values it binds, and replaces MVC's codeless
    /// automatic 400 with <see cref="InvalidModelStateRefusal.Refuse"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="SearchTerm"/> and <see cref="SortRequest"/> are validated when they are built: a
    /// value that exists is a valid one. MVC's validation visitor would otherwise read every public
    /// property of a bound object, and the members that belong to one state only
    /// (<see cref="SearchTerm.Value"/> of <see cref="SearchTerm.None"/>) refuse to be read in the
    /// other — which is the point of them, so the visitor is stopped rather than the guard removed.
    /// </remarks>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelModelBinding(this IServiceCollection services)
    {
        // First, so the complex-type binder MVC would otherwise pick for a struct never sees one.
        services.Configure<MvcOptions>(options =>
        {
            options.ModelBinderProviders.Insert(0, new PagingModelBinderProvider());
            options.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(SearchTerm)));
            options.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(SortRequest)));
        });

        // What MVC's own binders refuse (?page=abc, ?status=7) is refused with a code too, through
        // the same 400 a validator raises, instead of MVC's automatic answer that carries none.
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = InvalidModelStateRefusal.Refuse;
        });

        return services;
    }
}
