using Maran.Modules.Licensing.Services;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the Licensing module's one hosted service, the start-up licence check. Kept in its own
/// file, the same shape as <c>SeedingExtensions</c> and <c>BackgroundWorkExtensions</c>: this entry is
/// neither a startup seed nor a recurring or reconciling background job, it is a one-off observation
/// with no state of its own to write.
/// </summary>
/// <remarks>
/// <para>
/// The module's own services are registered like every other module's, by
/// <c>ModuleExtensions.AddPanelModules</c> walking <c>ModuleRegistry.All</c> (the module is in it now
/// that it has a screen), and its agent reach is checked there by <c>AgentCapabilityGuard</c>. Only the
/// hosted service is the Host's to add: a module may reference only the Sdk and the SharedKernel, so it
/// cannot register one.
/// </para>
/// </remarks>
public static class LicensingExtensions
{
    /// <summary>Adds the start-up licence check as a hosted service.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// A module cannot register its own hosted service — it may reference only the Sdk and the
    /// SharedKernel — so the Licensing module owns the CHECK (<see cref="LicenceStartupCheck"/>: what
    /// it reads, how it never throws) and this owns the fact that it runs. See
    /// <see cref="LicenceStartupCheck"/>'s own remarks for how §229 is held at this exact boundary.
    /// </remarks>
    public static IServiceCollection AddLicensingStartupCheck(this IServiceCollection services)
    {
        services.AddHostedService<LicenceStartupCheck>();

        return services;
    }
}
