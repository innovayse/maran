using Maran.Host.Seeding;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the reference data a fresh installation must have before anything can be created.
/// Listed explicitly, one line per seeder, so what a panel writes into its own database at startup
/// is visible in the composition root rather than discovered by scanning.
/// </summary>
public static class SeedingExtensions
{
    /// <summary>Adds every startup seed the panel runs.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// A module cannot register its own hosted service — it may reference only the Sdk and the
    /// SharedKernel — so a module owns the SEED and this owns the fact that it runs. That split is
    /// also the right one: which plans ship is the Accounts module's decision, and whether a panel
    /// seeds at startup is a composition decision.
    /// </remarks>
    public static IServiceCollection AddPanelSeeding(this IServiceCollection services)
    {
        services.AddHostedService<PlanSeedingStartupTask>();
        services.AddHostedService<FirewallWhitelistSeedingStartupTask>();
        services.AddHostedService<BackupDestinationSeedingStartupTask>();

        // Not reference data: it records the first instant this panel ran with the installer's
        // one-time setup token configured, which is what that token's expiry is counted from. It is
        // here rather than in the module because only the composition root may add a hosted service.
        services.AddHostedService<SetupTokenWindowStartupTask>();

        return services;
    }
}
