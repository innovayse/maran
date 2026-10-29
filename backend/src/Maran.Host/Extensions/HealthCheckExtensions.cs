using Maran.Host.BackgroundServices;
using Maran.Host.HealthChecks;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the probes the health endpoints depend on, and the startup check that reads the agent
/// probe once. The probes are plain services rather than framework health checks so their answers
/// stay ordinary values the endpoints can shape into the panel's own payload.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds the agent and database probes, and <see cref="AgentCompatibilityStartupCheck"/>, which
    /// states at startup whether the agent speaks a contract revision this panel drives.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="connectionString">Panel database connection string; empty when none is configured.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The startup check is registered here and not beside the other hosted services because it is the
    /// agent probe's own first reading: it takes <see cref="AgentHealthProbe"/> and nothing else, so the
    /// two cannot be composed apart.
    /// </remarks>
    public static IServiceCollection AddPanelHealthChecks(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(new DatabaseHealthProbe(connectionString));
        services.AddSingleton<AgentHealthProbe>();
        services.AddHostedService<AgentCompatibilityStartupCheck>();
        return services;
    }
}
