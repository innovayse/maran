using FluentValidation;
using Maran.Host.Modules;
using Maran.Sdk.Interfaces;

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
        // Before a single service is registered: a module that reaches for a part of the agent it
        // did not declare does not load at all. Placed first deliberately — half a composed panel
        // is a worse state to refuse from than none of one (AgentCapabilityGuard).
        AgentCapabilityGuard.Verify(ModuleRegistry.All);

        // Registered by the Host and not by any module, because it is the only place that knows all
        // of them: it answers whether the account-deletion cascade actually emptied every module's
        // rows, so a deletion completes on observed absence rather than on nothing having thrown.
        // Scoped, because it resolves the module contexts from the caller's own scope.
        services.AddScoped<IAccountResidueAuditor, ModuleAccountResidueAuditor>();

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
}
