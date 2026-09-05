using Maran.Host.BackgroundServices;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Tasks.Services;

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

        // Not a cadence but a one-off: both families' nftables units flush the ruleset on stop, and
        // the agent keeps no ban state, so without this line every firewall ban the panel has placed
        // ends silently at the next restart. The Firewall module owns the pass — it reads its own
        // rows and talks to its own agent client — and this owns the fact that it runs at startup.
        services.AddHostedService<StartupBanReconciler>();

        // Also a one-off, and for the mirror-image reason: a task is opened by one process and
        // closed by the same one, so a process that stops in between leaves its row Running for
        // ever and the panel shows an operation in flight that died with it. The Tasks module owns
        // the pass — it reads and closes its own rows — and this owns the fact that it runs at
        // startup.
        services.AddHostedService<StartupTaskReconciler>();

        // A cadence again, the same shape as CertificateRenewalScheduler: without it
        // tasks.PanelTasks accumulates one row per instrumented operation for as long as the panel
        // runs, because nothing else ever removes one. The Tasks module owns the JOB — it decides
        // the window and does the deleting — and this owns the CADENCE it runs on.
        services.AddHostedService<TaskRetentionScheduler>();

        // The one cadence that produces data rather than removing it: without this line the
        // Monitoring module's charts are permanently empty, because nothing else ever writes a
        // sample. The module owns the ROUND — it talks to the agent, stores the row and evaluates
        // the alerts — and this owns how often it runs.
        services.AddHostedService<MetricsSampler>();

        // And its counterweight, the same shape as the two above: monitoring.Samples gains a row a
        // minute and nothing else ever removes one, so without this the table grows for as long as
        // the panel runs. The module owns the JOB (the seven-day window of R10) and this the CADENCE.
        services.AddHostedService<MetricsRetentionScheduler>();

        return services;
    }
}
