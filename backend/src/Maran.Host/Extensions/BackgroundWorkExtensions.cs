using Maran.Host.BackgroundServices;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the panel's own recurring work. Listed explicitly, one line per service, so that work
/// which runs unattended is visible in the composition root rather than discovered by scanning.
/// </summary>
public static class BackgroundWorkExtensions
{
    /// <summary>Adds every hosted service the panel runs on a schedule.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// A module cannot register its own hosted service: a module may reference only the Sdk and the
    /// SharedKernel, and the schedule is Host composition. So the Ssl module owns the JOB and this
    /// owns the CADENCE, which is also the right split — an operator changing how often renewal runs
    /// is changing a deployment decision, not the module's behaviour.
    /// </remarks>
    public static IServiceCollection AddPanelBackgroundWork(this IServiceCollection services)
    {
        services.AddHostedService<CertificateRenewalScheduler>();

        return services;
    }
}
