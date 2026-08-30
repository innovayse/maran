using FluentValidation;
using Maran.Host.Modules;

namespace Maran.Host.Extensions;

/// <summary>
/// Loads the compiled-in modules. The registry is an explicit list, never assembly scanning, so
/// load order is readable and a module cannot appear by accident (rules/architecture.md).
/// </summary>
public static class ModuleExtensions
{
    /// <summary>Lets every registered module contribute its services.</summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">Configuration modules read their own settings from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        foreach (var module in ModuleRegistry.All)
        {
            module.ConfigureServices(services, configuration);

            // Discovered from the module's own assembly rather than registered by hand inside it:
            // a validator that exists but is not registered silently never runs, and the module
            // still compiles, still has passing validator tests, and still accepts bad input.
            services.AddValidatorsFromAssembly(module.GetType().Assembly, includeInternalTypes: false);
        }

        return services;
    }

    /// <summary>Lets every registered module map its endpoints.</summary>
    /// <param name="endpoints">The endpoint route builder modules map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPanelModules(this IEndpointRouteBuilder endpoints)
    {
        foreach (var module in ModuleRegistry.All)
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
