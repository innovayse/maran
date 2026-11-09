using Maran.Host.Configuration;
using Maran.Host.Dashboard;

namespace Maran.Host.Extensions;

/// <summary>Registers what the live dashboard stream needs from the composition root.</summary>
public static class DashboardStreamExtensions
{
    /// <summary>Binds the stream's bounds and registers its gate and its administrator source.</summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">Configuration the bounds bind from.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Both are singletons: the gate IS the process-wide count of open streams, and the source holds
    /// only the shared feed, the clock and the validated bounds. The bounds are validated at start, so a
    /// zero limit fails the boot instead of silently refusing every stream or none.
    /// </remarks>
    public static IServiceCollection AddPanelDashboardStream(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DashboardStreamOptions>()
            .Bind(configuration.GetSection(DashboardStreamOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<DashboardStreamGate>();
        services.AddSingleton<HostDashboardStreamSource>();

        return services;
    }
}
