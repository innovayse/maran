using Maran.Modules.Licensing;
using Maran.Modules.Licensing.Services;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the Licensing module's own services and its one hosted service — WITHOUT going through
/// <c>Maran.Host.Modules.ModuleRegistry.All</c>. Kept in its own file, the same shape as
/// <c>SeedingExtensions</c> and <c>BackgroundWorkExtensions</c>, rather than folded into either: this
/// entry is neither a startup seed nor a recurring or reconciling background job — it is a one-off
/// observation with no state of its own to write — so it did not read clearly filed under a heading
/// that already means something else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <see cref="LicensingModule.ConfigureServices"/> is called from here rather than from the
/// registry.</b> <c>ModuleRegistry.All</c>'s own comment explains, in full, why Licensing is
/// deliberately absent from that list: a module in it is a promise to the SPA's own composition
/// check that a sidebar destination exists, and licensing has none. But that list is also what
/// <c>ModuleExtensions.AddPanelModules</c> walks to call every module's own
/// <c>ConfigureServices</c>, and Wolverine's own discovery (<c>MessagingExtensions</c>) walks the
/// same list for its handler scan — so a module absent from it gets neither its DI registrations
/// nor a Wolverine route for free. This module needs the first (its controller resolves
/// <c>GetLicenceStatusQueryHandler</c> and its own dependencies straight from the container) and
/// deliberately not the second (see <c>Controllers.LicensingController</c>'s own remarks: its one
/// handler is resolved by ordinary constructor injection, not dispatched through
/// <c>IMessageBus</c>), so this method calls <see cref="LicensingModule.ConfigureServices"/>
/// directly, the same way this file already special-cases the hosted service below.
/// </para>
/// </remarks>
public static class LicensingExtensions
{
    /// <summary>Adds the Licensing module's own DI registrations and its startup licence check.</summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">Configuration the module reads its own settings from.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// A module cannot register its own hosted service — it may reference only the Sdk and the
    /// SharedKernel — so the Licensing module owns the CHECK (<see cref="LicenceStartupCheck"/>: what
    /// it reads, how it never throws) and this owns the fact that it runs. See
    /// <see cref="LicenceStartupCheck"/>'s own remarks for how §229 is held at this exact boundary.
    /// </remarks>
    public static IServiceCollection AddLicensingStartupCheck(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        new LicensingModule().ConfigureServices(services, configuration);
        services.AddHostedService<LicenceStartupCheck>();

        return services;
    }
}
