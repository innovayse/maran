using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace Maran.Modules.Monitoring.Services;

/// <summary>
/// Decides, from one round of readings, whether anything just went wrong or just came back — and
/// sends exactly one mail when it did.
/// </summary>
/// <remarks>
/// <para>
/// <b>It holds no memory of its own.</b> Everything it knows about what has already been reported
/// lives in <c>monitoring.AlertStates</c>, which is what makes the deduplication survive a restart:
/// an evaluator that counted in a field would forget an open episode on every deployment and mail
/// about it again ten samples later.
/// </para>
/// <para>
/// <b>The state is written before the mail is requested, and that order is deliberate.</b> A mail
/// server that is down must not make the panel re-raise the same alert on the next sample and every
/// sample after — that is the mail storm this class exists to prevent, arriving all at once the
/// moment the mail server recovers. So the transition is committed either way, and an alert with
/// nobody to tell is journalled as <c>MailSkippedNoSmtp</c> rather than retried.
/// </para>
/// <para>
/// <b>It does not send the mail; it asks for it.</b> This class composes the subject and body from
/// its own resources — the publisher owns the words, because only the publisher knows what the mail
/// is about — and publishes <see cref="SendMailRequested"/>, exactly as Identity's password reset
/// does. It has no mailer, no SMTP settings and no privileged position: outgoing mail belongs to the
/// Notifications module, and this is an ordinary consumer of it. The one thing it must read from
/// over there is WHERE an operator alert goes, which no caller of this class could supply — there is
/// no signed-in user at all — so it comes through <see cref="IAlertRecipientDirectory"/>, the Sdk's
/// read-only window onto that single field.
/// </para>
/// <para>
/// <b>A service the agent could not judge is not observed at all.</b> <c>Unknown</c> is neither an
/// outage nor proof of health — a socket-activated unit nothing has connected to, a unit
/// mid-transition, a unit not installed — so it neither advances the counter nor resets it. Treating
/// it as healthy would silently close an open episode; treating it as stopped would mail about every
/// Debian host's SSH socket at every reboot, which is the exact alert the agent's tri-state exists to
/// avoid producing.
/// </para>
/// </remarks>
public sealed class AlertEvaluator
{
    /// <summary>Above this percentage of the root filesystem, the disk is considered to be in alarm.</summary>
    /// <remarks>
    /// Ninety per cent, from spec §11. Strictly above, so a filesystem sitting at exactly ninety is
    /// not an alarm: the threshold marks the point where the remaining space has started to run out,
    /// and a server parked precisely on it is not yet losing anything.
    /// </remarks>
    public const double DiskUsageThresholdPercent = 90.0;

    /// <summary>The subject recorded and mailed about for the filesystem the panel watches.</summary>
    /// <remarks>
    /// The root filesystem, and only it — the agent measures one, because a hosting server keeps its
    /// accounts, its databases and its logs there. The constant is the alert row's subject, so it is
    /// also what makes the row stable across restarts.
    /// </remarks>
    public const string RootFilesystemSubject = "/";

    /// <summary>The subject recorded and mailed about for the sshd SFTP jail block.</summary>
    /// <remarks>
    /// There is exactly one block to watch — the installer writes one, for one group, at one fixed
    /// path — so this is a constant rather than something the agent's answer supplies, the same
    /// reason <see cref="RootFilesystemSubject"/> is.
    /// </remarks>
    public const string SftpJailSubject = "sshd-sftp-jail";

    /// <summary>The subject recorded and mailed about for the filesystem holding hosting accounts' homes.</summary>
    /// <remarks>
    /// There is exactly one such filesystem in today's supported layout — <c>/home</c>, matching the
    /// agent's own <c>AgentPaths::ACCOUNT_HOME_ROOT</c> — so this is a constant rather than something
    /// an agent reading supplies, the same reason <see cref="RootFilesystemSubject"/> and
    /// <see cref="SftpJailSubject"/> are.
    /// </remarks>
    public const string AccountHomeFilesystemSubject = "/home";

    /// <summary>The subject recorded and mailed about when an installed file differs from the release.</summary>
    /// <remarks>
    /// Distinct from <see cref="HashListSubject"/> deliberately: a mismatched file and a hash list
    /// the closed PluginLoader could not even read are two different operator questions, and folding
    /// them into one subject would mean a resolved hash-list-availability problem could look, in the
    /// journal, like a resolved file drift that never actually happened.
    /// </remarks>
    public const string InstalledFilesSubject = "installed-files";

    /// <summary>The subject recorded and mailed about when the code-integrity hash list itself could not be used.</summary>
    /// <remarks>See <see cref="InstalledFilesSubject"/> for why this is a subject of its own.</remarks>
    public const string HashListSubject = "hash-list";

    /// <summary>How many differing paths a code-integrity alert's mail body names before it switches to a count.</summary>
    /// <remarks>
    /// An operator reading a mail about a handful of files needs the files; an operator reading a
    /// mail about thousands needs a count and "see the panel", not a wall of text — the same
    /// order-of-magnitude reasoning docs/superpowers/plans/2026-09-19-maran-code-integrity.md Task 3
    /// applies to the mail body's cap.
    /// </remarks>
    public const int DifferingPathsMailCap = 50;

    /// <summary>How many differing paths a code-integrity alert's audit subject names, per the journal's own "count, never everything" doctrine.</summary>
    /// <remarks>
    /// Shorter than <see cref="DifferingPathsMailCap"/> on purpose: the journal is not the detailed
    /// report, matching
    /// <c>RepairDatabaseGrantsCommandHandler</c>'s own precedent that a journal subject is a count,
    /// never a dump. Unlike that precedent, naming a few of the paths here leaks nothing — every path
    /// is the panel's own code, not another tenant's data — so a short bounded list is included
    /// alongside the count rather than the count alone.
    /// </remarks>
    public const int DifferingPathsAuditCap = 10;

    /// <summary>The module's database context, which owns the alert rows.</summary>
    private readonly MonitoringDbContext _dbContext;

    /// <summary>Where an operator alert is addressed, read from the module that owns mail settings.</summary>
    private readonly IAlertRecipientDirectory _recipients;

    /// <summary>The bus a request to send is published on, for the Notifications module to pick up.</summary>
    private readonly IMessageBus _bus;

    /// <summary>The panel's append-only journal.</summary>
    private readonly MonitoringAuditJournal _journal;

    /// <summary>The subjects and bodies of the mail this class sends, in the panel's languages.</summary>
    private readonly IStringLocalizer<NotificationMessages> _text;

    /// <summary>Creates the evaluator.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="recipients">Where an operator alert is addressed.</param>
    /// <param name="bus">The bus a request to send is published on.</param>
    /// <param name="journal">The panel's append-only journal.</param>
    /// <param name="text">The localized subjects and bodies of the alert mail.</param>
    public AlertEvaluator(
        MonitoringDbContext dbContext,
        IAlertRecipientDirectory recipients,
        IMessageBus bus,
        MonitoringAuditJournal journal,
        IStringLocalizer<NotificationMessages> text)
    {
        _dbContext = dbContext;
        _recipients = recipients;
        _bus = bus;
        _journal = journal;
        _text = text;
    }

    /// <summary>Evaluates one round of readings and sends whatever mail it turns out to owe.</summary>
    /// <param name="diskUsedPercent">
    /// How full the root filesystem is, or <c>null</c> when the agent reported a capacity of zero and
    /// no percentage can be computed. A null is not a healthy observation: it advances nothing and
    /// resets nothing, because the panel did not find out.
    /// </param>
    /// <param name="services">The service statuses the agent reported, which may be an empty list.</param>
    /// <param name="sftpJailStatus">
    /// What the agent found when it checked the live sshd configuration for the installer's `Match
    /// Group` block, or <c>null</c> when that call did not succeed. A <c>null</c> advances nothing
    /// and resets nothing, for the same reason a <c>null</c> <paramref name="diskUsedPercent"/>
    /// does: the panel did not find out, and a call that failed to ask the question is not evidence
    /// about the answer.
    /// </param>
    /// <param name="quotaStatus">
    /// What the agent found when it checked whether the filesystem holding hosting accounts' homes
    /// can enforce a disk quota, or <c>null</c> when that call did not succeed. A <c>null</c>
    /// advances nothing and resets nothing, for the same reason a <c>null</c>
    /// <paramref name="sftpJailStatus"/> does: a check the agent could not answer is not evidence
    /// about the answer, and an alert built on it would teach an operator to ignore the alert.
    /// </param>
    /// <param name="codeIntegrityReport">
    /// The closed PluginLoader's latest finding about whether the panel's installed files match the
    /// release's signed hash list, or <c>null</c> when nothing has reported this round. A
    /// <c>null</c> advances nothing and resets nothing, for the same reason a <c>null</c>
    /// <paramref name="sftpJailStatus"/> does — including that it must not RESOLVE an alert that is
    /// currently firing: an unanswered round is not evidence the condition cleared.
    /// </param>
    /// <param name="observedAt">When the readings were taken, from the panel's clock.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    public async Task EvaluateAsync(
        double? diskUsedPercent,
        IReadOnlyList<AgentServiceStatus> services,
        AgentSftpJailStatus? sftpJailStatus,
        AgentQuotaEnforceability? quotaStatus,
        CodeIntegrityReport? codeIntegrityReport,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var pending = new List<(AlertKind Kind, string Subject, string AuditSubject, AlertTransition Transition, string Detail)>();

        if (diskUsedPercent is not null)
        {
            var transition = await ObserveAsync(
                AlertKind.DiskUsage,
                RootFilesystemSubject,
                diskUsedPercent.Value > DiskUsageThresholdPercent,
                observedAt,
                cancellationToken);

            pending.Add((AlertKind.DiskUsage, RootFilesystemSubject, RootFilesystemSubject, transition, FormatPercent(diskUsedPercent.Value)));
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

            pending.Add((AlertKind.ServiceStopped, name, name, transition, service.Detail));
        }

        if (sftpJailStatus is not null)
        {
            var transition = await ObserveAsync(
                AlertKind.SftpJailDrifted,
                SftpJailSubject,
                sftpJailStatus.IsDrifted,
                observedAt,
                cancellationToken);

            pending.Add((
                AlertKind.SftpJailDrifted,
                SftpJailSubject,
                SftpJailSubject,
                transition,
                FormatMissing(sftpJailStatus.Missing)));
        }

        if (quotaStatus is not null)
        {
            var transition = await ObserveAsync(
                AlertKind.QuotaNotEnforceable,
                AccountHomeFilesystemSubject,
                !quotaStatus.IsEnforceable,
                observedAt,
                cancellationToken);

            pending.Add((
                AlertKind.QuotaNotEnforceable,
                AccountHomeFilesystemSubject,
                AccountHomeFilesystemSubject,
                transition,
                FormatReason(quotaStatus.Reason)));
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
                    HashListSubject,
                    breaching: true,
                    observedAt,
                    cancellationToken);

                pending.Add((
                    AlertKind.CodeIntegrityDrifted,
                    HashListSubject,
                    HashListSubject,
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
                    InstalledFilesSubject,
                    breaching: drifted,
                    observedAt,
                    cancellationToken);

                pending.Add((
                    AlertKind.CodeIntegrityDrifted,
                    InstalledFilesSubject,
                    drifted
                        ? FormatDifferingPathsAuditSubject(codeIntegrityReport, InstalledFilesSubject)
                        : InstalledFilesSubject,
                    filesTransition,
                    drifted
                        ? FormatDifferingPathsBody(codeIntegrityReport.DifferingPaths)
                        : string.Empty));

                // Clean also resolves whichever hash-list-availability episode was open. A Clean
                // comparison is proof the hash list WAS read successfully this round, so it is
                // exactly the evidence needed to close that alert too — otherwise a
                // hash-list-availability alert that has nothing to do with drift would never
                // resolve once drift also clears.
                var hashListTransition = await ObserveAsync(
                    AlertKind.CodeIntegrityDrifted,
                    HashListSubject,
                    breaching: false,
                    observedAt,
                    cancellationToken);

                pending.Add((
                    AlertKind.CodeIntegrityDrifted,
                    HashListSubject,
                    HashListSubject,
                    hashListTransition,
                    string.Empty));
            }
        }

        // One save for the whole round, before any mail is attempted. See the type's remarks: a
        // transition that was not committed is a transition that repeats on the next sample.
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var (kind, subject, auditSubject, transition, detail) in pending)
        {
            if (transition == AlertTransition.None)
            {
                continue;
            }

            await AnnounceAsync(kind, subject, auditSubject, transition, detail, cancellationToken);
        }
    }

    /// <summary>Renders the differing paths of a drifted code-integrity report for the mail body.</summary>
    /// <param name="differingPaths">The paths the closed PluginLoader reported as not matching.</param>
    /// <returns>
    /// Every path, comma-joined, up to <see cref="DifferingPathsMailCap"/>; beyond that, the first
    /// entries plus a count of how many more were not shown — an operator reading a mail about
    /// thousands of files needs a count and "see the panel", never a wall of text.
    /// </returns>
    private static string FormatDifferingPathsBody(IReadOnlyList<string> differingPaths)
    {
        if (differingPaths.Count <= DifferingPathsMailCap)
        {
            return string.Join(", ", differingPaths);
        }

        var shown = differingPaths.Take(DifferingPathsMailCap);
        return string.Join(", ", shown) + $", and {differingPaths.Count - DifferingPathsMailCap} more";
    }

    /// <summary>Renders the journal's <c>key=value</c> subject for a drifted code-integrity report.</summary>
    /// <param name="report">The report to summarize.</param>
    /// <param name="subject">The plain alert subject (<see cref="InstalledFilesSubject"/>).</param>
    /// <returns>
    /// <c>installed-files;version=&lt;version&gt;;count=&lt;n&gt;;paths=&lt;a,b,...&gt;</c> — the
    /// version and the count are never truncated (the count is the figure that matters most), the
    /// path list is capped at <see cref="DifferingPathsAuditCap"/>, following
    /// <c>RepairDatabaseGrantsCommandHandler</c>'s "the subject is a count, never everything"
    /// doctrine, in the <c>key=value;key=value</c> shape that same handler established so the
    /// journal reads the same regardless of the panel's active locale.
    /// </returns>
    private static string FormatDifferingPathsAuditSubject(CodeIntegrityReport report, string subject)
    {
        var capped = report.DifferingPaths.Take(DifferingPathsAuditCap);
        return $"{subject};version={report.InstalledVersion};count={report.DifferingPaths.Count};paths={string.Join(",", capped)}";
    }

    /// <summary>Renders a percentage for the body of an alert mail.</summary>
    /// <param name="percent">The percentage to render.</param>
    /// <returns>The value with one decimal place, in the invariant culture.</returns>
    /// <remarks>
    /// Invariant rather than the current culture, because this is a number inside a sentence that is
    /// itself localized: the sentence comes from the resx in the reader's language, while the figure
    /// has one unambiguous spelling. A background sender has no request culture to read anyway.
    /// </remarks>
    private static string FormatPercent(double percent)
    {
        return percent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Renders what the agent found missing, for the body of a jail-drift alert mail.</summary>
    /// <param name="missing">
    /// What the agent's block check did not find — the installer's own words, empty when the block
    /// is intact.
    /// </param>
    /// <returns>A comma-separated list, or a fixed sentence when nothing is missing.</returns>
    /// <remarks>
    /// The resolved mail does not need this detail — it says the block is back — but the same
    /// pending row is built for both directions, so this must answer sensibly for an empty list
    /// too rather than being read only on the raising path.
    /// </remarks>
    private static string FormatMissing(IReadOnlyList<string> missing)
    {
        return missing.Count == 0 ? string.Empty : string.Join(", ", missing);
    }

    /// <summary>Renders why the filesystem cannot enforce a quota, for the body of the alert mail.</summary>
    /// <param name="reason">The agent's own classification.</param>
    /// <returns>A short, invariant phrase naming the reason.</returns>
    /// <remarks>
    /// The resolved mail does not read this — it says enforcement is back — but the same pending row
    /// is built for both directions, so this must answer sensibly for
    /// <see cref="QuotaUnenforceableReason.Unspecified"/> too rather than being read only on the
    /// raising path.
    /// </remarks>
    private static string FormatReason(QuotaUnenforceableReason reason)
    {
        return reason switch
        {
            QuotaUnenforceableReason.MountedWithoutQuotaAccounting => "not mounted with quota accounting",
            QuotaUnenforceableReason.AccountingNotEnabled => "quota accounting is off",
            _ => "unknown",
        };
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

    /// <summary>Journals a transition and mails about it.</summary>
    /// <param name="kind">Which kind of condition changed.</param>
    /// <param name="subject">Which thing of that kind — used for the mail's subject/body formatting.</param>
    /// <param name="auditSubject">
    /// What is written to the journal for this transition. Equal to <paramref name="subject"/> for
    /// every existing kind; for a drifted code-integrity report it additionally carries the
    /// installed version, the differing-file count, and a capped path list in <c>key=value</c> form
    /// (see <see cref="FormatDifferingPathsAuditSubject"/>) — the journal outlives the locale the
    /// writer happened to be using, so this is never a localized sentence.
    /// </param>
    /// <param name="transition">Whether the episode opened or closed.</param>
    /// <param name="detail">The figure or the service manager's words that go in the body.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <remarks>
    /// The journal entry is written whatever happens to the mail, and it is written first: "the disk
    /// filled at 02:14" is a fact about the server, while "the mail about it was delivered" is a fact
    /// about the mail server, and an operator investigating an outage needs the first one even when —
    /// especially when — the second never happened. <c>MailSkippedNoSmtp</c> is still recorded here,
    /// because "there was nobody to tell" is this class's own finding; a send that was requested and
    /// then refused is journalled by the module that attempted it.
    /// </remarks>
    private async Task AnnounceAsync(
        AlertKind kind,
        string subject,
        string auditSubject,
        AlertTransition transition,
        string detail,
        CancellationToken cancellationToken)
    {
        var action = transition == AlertTransition.Raised ? AuditActions.AlertRaised : AuditActions.AlertResolved;
        await _journal.RecordSystemAsync(action, $"{kind}:{auditSubject}", succeeded: true, cancellationToken);

        var recipient = await _recipients.GetAlertRecipientAsync(cancellationToken);
        if (recipient is null)
        {
            await _journal.RecordSystemAsync(
                AuditActions.MailSkippedNoSmtp, $"{kind}:{auditSubject}", succeeded: false, cancellationToken);
            return;
        }

        var subjectLine = _text[SubjectKey(kind, transition), subject].Value;
        var body = _text[BodyKey(kind, transition), subject, detail].Value;

        // Published, not sent. Whether the mail server accepted it is the sending module's business
        // and is journalled there; this class's business is that the transition was recorded and the
        // request was made. Publishing also keeps the sampler's round off a multi-second SMTP round
        // trip to somebody else's server.
        await _bus.PublishAsync(new SendMailRequested(recipient, subjectLine, body));
    }

    /// <summary>The resource key of the subject line for one kind of transition.</summary>
    /// <param name="kind">Which kind of condition changed.</param>
    /// <param name="transition">Whether the episode opened or closed.</param>
    /// <returns>The key in <c>NotificationMessages.resx</c>.</returns>
    private static string SubjectKey(AlertKind kind, AlertTransition transition)
    {
        return $"Alert{kind}{transition}Subject";
    }

    /// <summary>The resource key of the body for one kind of transition.</summary>
    /// <param name="kind">Which kind of condition changed.</param>
    /// <param name="transition">Whether the episode opened or closed.</param>
    /// <returns>The key in <c>NotificationMessages.resx</c>.</returns>
    private static string BodyKey(AlertKind kind, AlertTransition transition)
    {
        return $"Alert{kind}{transition}Body";
    }
}
