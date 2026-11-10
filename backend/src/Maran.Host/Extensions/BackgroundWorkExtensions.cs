using Maran.Host.BackgroundServices;
using Maran.Modules.Backups.Services;
using Maran.Modules.Dns.Services;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Sites.Services;
using Maran.Modules.Tasks.Services;
using Maran.Modules.Updates.Services;

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

        // The same split for the administrators' notification feed: the Notifications module owns the
        // JOB (ninety days, five hundred entries) and this the CADENCE. Without it the feed's table
        // grows for as long as the panel runs.
        services.AddHostedService<NotificationRetentionScheduler>();

        // The audit journal's upkeep, hourly because the part that matters most is sealing: a row is
        // outside the hash chain's protection until the tick seals it, so the interval is the length
        // of that window. The once-a-day work (retention, the orphan-actor sweep, chain verification)
        // is gated by the Identity module's handler against the time it stored for its last run, so a
        // restart neither skips a day nor repeats one. The module owns the JOB and this the CADENCE;
        // without this line the journal is never sealed and a saved retention window deletes nothing.
        services.AddHostedService<AuditMaintenanceScheduler>();

        // The hourly purge of expired idempotency records, the same split: the Idempotency module owns
        // the JOB (what has expired, the batch size) and this owns the CADENCE. Hourly and not daily
        // because a reservation's lease is five minutes and a stored response lives 24 hours, so a
        // daily pass would let a table of customer data at rest in clear outlive its promise by a day.
        services.AddHostedService<IdempotencyPurgeScheduler>();

        // The one cadence that produces data rather than removing it: without this line the
        // Monitoring module's charts are permanently empty, because nothing else ever writes a
        // sample. The module owns the ROUND — it talks to the agent, stores the row and evaluates
        // the alerts — and this owns how often it runs.
        services.AddHostedService<MetricsSampler>();

        // The dashboard stream's source of readings (issue #199). Unlike the sampler above it polls the agent
        // only while a dashboard is open, and sleeps otherwise, so a panel nobody is watching costs the
        // agent nothing. The Monitoring module owns the ROUND and the ring; this owns that it runs.
        services.AddHostedService<LiveMetricsPoller>();

        // And its counterweight, the same shape as the two above: monitoring.Samples gains a row a
        // minute and nothing else ever removes one, so without this the table grows for as long as
        // the panel runs. The module owns the JOB (the seven-day window of R10) and this the CADENCE.
        services.AddHostedService<MetricsRetentionScheduler>();

        // The hourly pass over every site's logs: the cursor read of each access log into the Traffic
        // tab's daily figures, then the removal of rotated archives beyond each site's own retention
        // (issue #152). The Sites module owns the JOB and this owns the CADENCE; without this line the
        // tab stays empty and a retention a customer chose is never applied after the moment it is saved.
        services.AddHostedService<SiteLogMaintenanceScheduler>();

        // The one cadence that is not daily, because a backup schedule names an hour of the day and
        // a daily tick could not see it. Without this line the Backups module's schedules never fire
        // and its retention never runs — so backups.Backups and the archives underneath it grow by
        // one whole account per manual backup and are bounded by nothing at all. The module owns the
        // SWEEP (which schedules are due, what a run writes, what retention prunes) and this owns how
        // often it is asked.
        services.AddHostedService<BackupScheduleScheduler>();

        // The hourly removal of account exports whose retention has ended (issue #127): the artifact in the agent's
        // root-only directory is a plaintext copy of a customer's data, and the screen promises it is gone after
        // seven days. The ServerMigration module owns the JOB and this owns the CADENCE.
        services.AddHostedService<AccountExportExpiryScheduler>();

        // A one-off, and the third of that shape here — but the only one that ASKS the host a
        // question instead of reading the panel's own rows. The agent's CreateBackup runs to
        // completion on a detached task, so a panel process killed mid-stream leaves a Running
        // backup row that nothing moves, and such a row refuses every restore of that account for
        // ever. The Backups module owns the PASS — it reads its own rows and calls ListBackups on
        // its own agent client to observe what is really on the destination — and this owns the
        // fact that it runs at startup. Registered here and not in BackupsModule for the reason
        // stated at the top of this file, and its absence would be the worst kind of nothing: the
        // class would exist, the doc comments would describe a reclamation, and no backup would
        // ever be reclaimed.
        services.AddHostedService<StartupBackupReconciler>();

        // The daily purge of every account's file-manager trash beyond its retention. The Files module owns
        // the JOB (which accounts, what a failing account does to the rest) and this owns the CADENCE;
        // without this line a trashed entry stays for ever and keeps counting toward its account's quota.
        services.AddHostedService<TrashRetentionScheduler>();

        // A one-off for the layout a site was written in: the agent moved the PHP-FPM sockets to a
        // new directory, and a pool and vhost rendered by an older agent still name the old one, so
        // the site answers 502 until something re-renders it. The Sites module owns the PASS — it
        // reads its own rows and asks its own agent client — and this owns the fact that it runs at
        // every start, because the panel records nothing about which site was converged.
        services.AddHostedService<StartupPhpSiteReconciler>();

        // A one-off for the name server: the database is the truth for every zone and the daemon serves files
        // derived from it, so a zone served at a serial that differs from its row is published again and a
        // served zone with no row is removed. The Dns module owns the PASS (what differs, which side wins,
        // the lock an edit takes) and this owns the fact that it runs at every start, because the panel
        // records nothing about which zone was reconciled. Without this line a save that failed after the
        // agent applied an edit would leave the daemon serving data the database does not have, for ever.
        services.AddHostedService<DnsZoneReconciler>();

        // The hourly sweep of certificate validation records an order left behind when its process died
        // between publishing a DNS-01 challenge and removing it (issue #161). The Dns module owns the PASS
        // (which records are stale, the lock an edit takes) and this owns the cadence; without this line a
        // record published for a validation that never finished would stay in the customer's zone for ever.
        services.AddHostedService<DnsChallengeSweeper>();

        // A one-off that reads a CHOICE rather than repairing state: an administrator's runtime log
        // level lives in the logger's switch, which a restart resets to the configured level. The
        // Monitoring module owns the stored choice and this owns the fact that it is applied at
        // every start (issue #131).
        services.AddHostedService<StartupLogLevelRestorer>();

        // The pass that closes a panel-update task whose report never arrived (issue #382): the root
        // unit never started, or its report command failed. The task is kept across the restart the
        // update itself causes, so nothing else would ever close it. The Updates module owns the PASS
        // and its thresholds; this owns the fact that it runs.
        services.AddHostedService<PanelUpdateTaskReaper>();

        // The pass that writes a module's licence lapse and recovery to the audit journal. It has to run
        // outside a request: the journal stamps a customer caller's account on its entries, and a lapse
        // is the whole server's fact. The Licensing module owns the PASS (what is a lapse, what the
        // entry carries) and this owns the fact that it runs; it is resolved from the module's own
        // singleton registration so the pass the host runs and the pass a test drives are one object.
        services.AddHostedService(provider => { return provider.GetRequiredService<LicenceCoverageWatcher>(); });

        // The daily renewal of the licence lease, the only thing that writes lease.json and, with the
        // marketplace, one of the two things that leave the server on this panel's own account
        // (rules/security.md item 10, fifth exception). Without this line every online licence lapses at the
        // end of its last lease and a paid module's download is refused as LeaseRequired. The Licensing module
        // owns the PASS (when a lease is due, what answer is accepted, what is written) and its schedule's rule;
        // this owns the fact that it runs. It does nothing for an offline licence or with no licence installed,
        // and is resolved from the module's singleton so the worker the host runs and the one a test drives are
        // one object.
        services.AddHostedService(provider => { return provider.GetRequiredService<LeaseRenewalWorker>(); });

        return services;
    }
}
