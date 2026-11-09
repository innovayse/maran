using Maran.Host.Search;

namespace Maran.Host.Extensions;

/// <summary>Registers the global search's fan-out.</summary>
public static class SearchExtensions
{
    /// <summary>
    /// Registers <see cref="SearchFanOut"/>, which gathers every <c>ISearchProvider</c> the composed modules
    /// registered. Scoped, because the providers it holds are scoped (they read the request's tenant-scoped
    /// contexts).
    /// </summary>
    /// <param name="services">The service collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelSearch(this IServiceCollection services)
    {
        services.AddScoped<SearchFanOut>();
        return services;
    }
}
