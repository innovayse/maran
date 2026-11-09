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
    /// <summary>Lets every module that ships with the panel contribute its services.</summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">Configuration modules read their own settings from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddPanelModules(
            configuration,
            PackagedModuleComposer.Compose(ModuleRegistry.All, [], new PanelVersion()));
    }

    /// <summary>
    /// Lets every composed module contribute its services: the ones that ship with the panel and the
    /// packaged ones that passed <see cref="PackagedModuleComposer"/>. The composition is decided by the
    /// caller, once, because message handlers are registered from the same list when the host is built
    /// and the two must not disagree about which modules exist.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">Configuration modules read their own settings from.</param>
    /// <param name="composition">What this start composed and refused.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelModules(
        this IServiceCollection services,
        IConfiguration configuration,
        PackagedModuleComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        var modules = composition.Composed;

        // Before a single service is registered: a module that reaches for a part of the agent it
        // did not declare does not load at all. Placed first deliberately — half a composed panel
        // is a worse state to refuse from than none of one (AgentCapabilityGuard).
        AgentCapabilityGuard.Verify(modules);

        // Likewise before any service: a module whose manifest breaks the module contract (a
        // dependency missing, out of its range or in a loop, a panel too old, a permission outside
        // the vocabulary) must not start against a neighbour it was never written for.
        ModuleManifestGuard.Verify(
            modules.Select(module => { return module.Manifest; }).ToList(),
            new PanelVersion());

        // The Sdk's window onto the version of the panel the modules are composed into. The Host's,
        // because only the composition root knows which assembly the panel is built from.
        services.AddSingleton<IPanelVersion, PanelVersion>();

        // The runtime view of the module set (IModuleRegistry), the audit journal the module loader
        // writes through, the restart a staged package waits for, and the record of what this start
        // refused. All of them are the Host's: only the composition root knows which modules it composed.
        services.AddSingleton(composition);
        services.AddSingleton<RuntimeModuleRegistry>();
        services.AddSingleton<IModuleRegistry>(provider =>
        {
            return provider.GetRequiredService<RuntimeModuleRegistry>();
        });
        services.AddHttpContextAccessor();
        services.AddScoped<IModuleLoaderAudit, ModuleLoaderAuditJournal>();
        services.AddSingleton<PanelRestartRequest>();
        services.AddSingleton<IPanelRestart>(provider =>
        {
            return provider.GetRequiredService<PanelRestartRequest>();
        });
        services.AddHostedService<PackagedModuleRefusalRecorder>();

        // Registered by the Host and not by any module, because it is the only place that knows all
        // of them: it answers whether the account-deletion cascade actually emptied every module's
        // rows, so a deletion completes on observed absence rather than on nothing having thrown.
        // Scoped, because it resolves the module contexts from the caller's own scope.
        services.AddScoped<IAccountResidueAuditor, ModuleAccountResidueAuditor>();

        // The Host's too: it alone reads the configured panel name, and answers the Sites, Ssl and Ftp
        // modules' one question, "is this name the panel's own", with a verdict and never the name —
        // and, through a second, separate window, gives the name itself to the Ssl module's
        // administrator-only panel certificate (issue #132). One instance behind both windows, so the
        // two can never disagree about what the name is. Singleton because it normalises the
        // configured value once and holds nothing else.
        services.AddSingleton<PanelHostDirectory>();
        services.AddSingleton<IReservedHostDirectory>(provider =>
        {
            return provider.GetRequiredService<PanelHostDirectory>();
        });
        services.AddSingleton<IPanelHostName>(provider =>
        {
            return provider.GetRequiredService<PanelHostDirectory>();
        });

        // Also the Host's and no module's: a licence tier is declared in the Sdk's module contract
        // and belongs to none of the modules that declare one, so no module's resources may name
        // it. The catalogue endpoint resolves it per request through this (LicenceTierDisplayNames);
        // singleton because the resolver holds only the localizer, which reads the ambient request
        // culture at lookup time rather than at construction.
        services.AddSingleton<LicenceTierDisplayNames>();

        // The module kind is the same kind of thing and is resolved the same way (ModuleKindDisplayNames).
        services.AddSingleton<ModuleKindDisplayNames>();

        foreach (var module in modules)
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
