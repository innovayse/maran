using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Mappers;
using Maran.Modules.Monitoring.Models;
using Maran.Modules.Monitoring.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Services;

/// <summary>
/// Records one round of readings against the alert rows and answers what each observation changed,
/// for <see cref="AlertEvaluator"/> to commit and announce.
/// </summary>
/// <remarks>
/// <para>
/// It saves nothing and mails nothing. The rows it creates or advances are tracked by the scoped
/// <see cref="MonitoringDbContext"/> it shares with the evaluator, which commits the whole round in
/// one save before any mail is requested — the order the evaluator's own remarks explain.
/// </para>
/// <para>
/// Every reading that is <c>null</c> — and every service whose state is <c>Unknown</c> — is skipped:
/// it advances nothing and resets nothing, because the panel did not find out (see
/// <see cref="AlertEvaluator.EvaluateAsync"/> for each input's reason).
/// </para>
/// </remarks>
public sealed class AlertObserver
{
    /// <summary>The module's database context, which owns the alert rows.</summary>
    private readonly MonitoringDbContext _dbContext;

    /// <summary>The panel's append-only journal, for a code-integrity report that cannot be trusted.</summary>
    private readonly MonitoringAuditJournal _journal;

    /// <summary>Creates the observer.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="journal">The panel's append-only journal.</param>
    public AlertObserver(MonitoringDbContext dbContext, MonitoringAuditJournal journal)
    {
        _dbContext = dbContext;
        _journal = journal;
    }

    /// <summary>Records every answered reading of one round against its alert row.</summary>
    /// <param name="diskUsedPercent">How full the root filesystem is, or <c>null</c> when unanswered.</param>
    /// <param name="freeSpaceFloor">The root filesystem's room beside the free-space floor, or <c>null</c>.</param>
    /// <param name="services">The service statuses the agent reported, which may be an empty list.</param>
    /// <param name="sftpJailStatus">The sshd SFTP jail block check, or <c>null</c> when unanswered.</param>
    /// <param name="quotaStatus">The account-home quota check, or <c>null</c> when unanswered.</param>
    /// <param name="codeIntegrityReport">The latest code-integrity finding, or <c>null</c> when none arrived.</param>
    /// <param name="observedAt">When the readings were taken, from the panel's clock.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>One entry per observation, in observation order, including those that changed nothing.</returns>
    public async Task<IReadOnlyList<PendingAlert>> ObserveRoundAsync(
        double? diskUsedPercent,
        FreeSpaceFloorReading? freeSpaceFloor,
        IReadOnlyList<AgentServiceStatus> services,
        AgentSftpJailStatus? sftpJailStatus,
        AgentQuotaEnforceability? quotaStatus,
        CodeIntegrityReport? codeIntegrityReport,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var pending = new List<PendingAlert>();

        if (diskUsedPercent is not null)
        {
            var transition = await ObserveAsync(
                AlertKind.DiskUsage,
                AlertEvaluator.RootFilesystemSubject,
                diskUsedPercent.Value > AlertEvaluator.DiskUsageThresholdPercent,
                observedAt,
                cancellationToken);

            pending.Add(new PendingAlert(
                AlertKind.DiskUsage,
                AlertEvaluator.RootFilesystemSubject,
                AlertEvaluator.RootFilesystemSubject,
                transition,
                AlertDetailMapper.FormatPercent(diskUsedPercent.Value)));
        }

        if (freeSpaceFloor is not null)
        {
            var transition = await ObserveAsync(
                AlertKind.FreeSpaceFloorApproaching,
                AlertEvaluator.RootFilesystemSubject,
                FreeSpaceFloorMargin.IsApproaching(freeSpaceFloor),
                observedAt,
                cancellationToken);

            pending.Add(new PendingAlert(
                AlertKind.FreeSpaceFloorApproaching,
                AlertEvaluator.RootFilesystemSubject,
                AlertEvaluator.RootFilesystemSubject,
                transition,
                AlertDetailMapper.FormatGibibytes(freeSpaceFloor.AvailableBytes)));
        }

        foreach (var service in services)
        {
            if (service.State == AgentServiceState.Unknown)
            {
                continue;
            }

            var name = service.Service.ToString();
            var transition = await ObserveAsync(
                AlertKind.ServiceStopped,
                name,
                service.State == AgentServiceState.Stopped,
                observedAt,
                cancellationToken);

            pending.Add(new PendingAlert(AlertKind.ServiceStopped, name, name, transition, service.Detail));
        }

        if (sftpJailStatus is not null)
        {
            var transition = await ObserveAsync(
                AlertKind.SftpJailDrifted,
                AlertEvaluator.SftpJailSubject,
                sftpJailStatus.IsDrifted,
                observedAt,
                cancellationToken);

            pending.Add(new PendingAlert(
                AlertKind.SftpJailDrifted,
                AlertEvaluator.SftpJailSubject,
                AlertEvaluator.SftpJailSubject,
                transition,
                AlertDetailMapper.FormatMissing(sftpJailStatus.Missing)));
        }

        if (quotaStatus is not null)
        {
            var transition = await ObserveAsync(
                AlertKind.QuotaNotEnforceable,
                AlertEvaluator.AccountHomeFilesystemSubject,
                !quotaStatus.IsEnforceable,
                observedAt,
                cancellationToken);

            pending.Add(new PendingAlert(
                AlertKind.QuotaNotEnforceable,
                AlertEvaluator.AccountHomeFilesystemSubject,
                AlertEvaluator.AccountHomeFilesystemSubject,
                transition,
                AlertDetailMapper.FormatReason(quotaStatus.Reason)));
        }

        if (codeIntegrityReport is not null)
        {
            if (codeIntegrityReport.Outcome == CodeIntegrityOutcome.Drifted
                && codeIntegrityReport.DifferingPaths.Count == 0)
            {
                // A contract violation: a correct comparison never reports Drifted with nothing
                // named. Never interpreted charitably as Clean, and never treated as Unavailable
                // either — that outcome is reserved for "the comparison could not run", not "the
                // closed side sent a report this repository cannot trust." Logged and otherwise
                // ignored this round: neither InstalledFilesSubject nor HashListSubject is observed
                // from it, so it can neither raise nor resolve either alert.
                await _journal.RecordSystemAsync(
                    AuditActions.CodeIntegrityReportRejected,
                    $"version={codeIntegrityReport.InstalledVersion};outcome=Drifted;paths=0",
                    succeeded: false,
                    cancellationToken);
            }
            else if (codeIntegrityReport.Outcome == CodeIntegrityOutcome.Unavailable)
            {
                var transition = await ObserveAsync(
                    AlertKind.CodeIntegrityDrifted,
                    AlertEvaluator.HashListSubject,
                    breaching: true,
                    observedAt,
                    cancellationToken);

                pending.Add(new PendingAlert(
                    AlertKind.CodeIntegrityDrifted,
                    AlertEvaluator.HashListSubject,
                    AlertEvaluator.HashListSubject,
                    transition,
                    codeIntegrityReport.UnavailableReason ?? string.Empty));

                // A report that could not run says nothing about whether the files themselves
                // differ. It must not implicitly resolve an already-open InstalledFilesSubject
                // episode — that would require an actual Clean comparison, which this round did not
                // get.
            }
            else
            {
                var drifted = codeIntegrityReport.Outcome == CodeIntegrityOutcome.Drifted;

                var filesTransition = await ObserveAsync(
                    AlertKind.CodeIntegrityDrifted,
                    AlertEvaluator.InstalledFilesSubject,
                    breaching: drifted,
                    observedAt,
                    cancellationToken);

                pending.Add(new PendingAlert(
                    AlertKind.CodeIntegrityDrifted,
                    AlertEvaluator.InstalledFilesSubject,
                    drifted
                        ? AlertDetailMapper.FormatDifferingPathsAuditSubject(
                            codeIntegrityReport,
                            AlertEvaluator.InstalledFilesSubject)
                        : AlertEvaluator.InstalledFilesSubject,
                    filesTransition,
                    drifted
                        ? AlertDetailMapper.FormatDifferingPathsBody(codeIntegrityReport.DifferingPaths)
                        : string.Empty));

                // Clean also resolves whichever hash-list-availability episode was open. A Clean
                // comparison is proof the hash list WAS read successfully this round, so it is
                // exactly the evidence needed to close that alert too — otherwise a
                // hash-list-availability alert that has nothing to do with drift would never
                // resolve once drift also clears.
                var hashListTransition = await ObserveAsync(
                    AlertKind.CodeIntegrityDrifted,
                    AlertEvaluator.HashListSubject,
                    breaching: false,
                    observedAt,
                    cancellationToken);

                pending.Add(new PendingAlert(
                    AlertKind.CodeIntegrityDrifted,
                    AlertEvaluator.HashListSubject,
                    AlertEvaluator.HashListSubject,
                    hashListTransition,
                    string.Empty));
            }
        }

        return pending;
    }

    /// <summary>Records one observation against its alert row, creating the row on first sight.</summary>
    /// <param name="kind">Which kind of condition was observed.</param>
    /// <param name="subject">Which thing of that kind.</param>
    /// <param name="breaching">Whether the observation found the condition unhealthy.</param>
    /// <param name="observedAt">When the observation was made.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What the observation changed.</returns>
    private async Task<AlertTransition> ObserveAsync(
        AlertKind kind,
        string subject,
        bool breaching,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var state = await _dbContext.AlertStates
            .FirstOrDefaultAsync(row => row.Kind == kind && row.Subject == subject, cancellationToken);

        if (state is null)
        {
            state = new AlertState(Guid.NewGuid(), kind, subject, observedAt);
            _dbContext.AlertStates.Add(state);
        }

        return state.Observe(breaching, observedAt);
    }
}
