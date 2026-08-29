using Maran.Host.HealthChecks;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the probes the health endpoints depend on. They are plain services rather than
/// framework health checks so their answers stay ordinary values the endpoints can shape into the
/// panel's own payload.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>Adds the agent and database probes.</summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="connectionString">Panel database connection string; empty when none is configured.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelHealthChecks(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(new DatabaseHealthProbe(connectionString));
        services.AddSingleton<AgentHealthProbe>();
        return services;
    }
}
