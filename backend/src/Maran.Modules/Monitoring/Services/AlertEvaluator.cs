using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Models;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Resources;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

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
/// is about — and hands them to <see cref="OperatorAlertMailer"/>, the panel's one path for an
/// operator alert. That path journals the transition, reads where an operator alert goes and
/// requests the mail; this class holds no mailer, no recipient and no message bus of its own.
/// Recording each reading against its alert row is <see cref="AlertObserver"/>'s.
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

    /// <summary>The module's database context, which owns the alert rows.</summary>
    private readonly MonitoringDbContext _dbContext;

    /// <summary>Records each reading against its alert row and answers what it changed.</summary>
    private readonly AlertObserver _observer;

    /// <summary>The panel's one path for an operator alert: journal, recipient, mail (issue #290).</summary>
    private readonly OperatorAlertMailer _operatorAlerts;

    /// <summary>The panel's append-only journal.</summary>
    private readonly MonitoringAuditJournal _journal;

    /// <summary>The subjects and bodies of the mail this class sends, in the panel's languages.</summary>
    private readonly IStringLocalizer<NotificationMessages> _text;

    /// <summary>Creates the evaluator.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="observer">Records each reading against its alert row.</param>
    /// <param name="operatorAlerts">The panel's one path for an operator alert.</param>
    /// <param name="journal">The panel's append-only journal.</param>
    /// <param name="text">The localized subjects and bodies of the alert mail.</param>
    public AlertEvaluator(
        MonitoringDbContext dbContext,
        AlertObserver observer,
        OperatorAlertMailer operatorAlerts,
        MonitoringAuditJournal journal,
        IStringLocalizer<NotificationMessages> text)
    {
        _dbContext = dbContext;
        _observer = observer;
        _operatorAlerts = operatorAlerts;
        _journal = journal;
        _text = text;
    }

    /// <summary>Evaluates one round of readings and sends whatever mail it turns out to owe.</summary>
    /// <param name="diskUsedPercent">
    /// How full the root filesystem is, or <c>null</c> when the agent reported a capacity of zero and
    /// no percentage can be computed. A null is not a healthy observation: it advances nothing and
    /// resets nothing, because the panel did not find out.
    /// </param>
    /// <param name="freeSpaceFloor">
    /// The root filesystem's available space beside the agent's free-space floor, or <c>null</c> when
    /// this round did not read it. A <c>null</c> advances nothing and resets nothing, for the reason
    /// a <c>null</c> <paramref name="diskUsedPercent"/> does.
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
        FreeSpaceFloorReading? freeSpaceFloor,
        IReadOnlyList<AgentServiceStatus> services,
        AgentSftpJailStatus? sftpJailStatus,
        AgentQuotaEnforceability? quotaStatus,
        CodeIntegrityReport? codeIntegrityReport,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var pending = await _observer.ObserveRoundAsync(
            diskUsedPercent,
            freeSpaceFloor,
            services,
            sftpJailStatus,
            quotaStatus,
            codeIntegrityReport,
            observedAt,
            cancellationToken);

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

    /// <summary>Journals a transition and mails about it.</summary>
    /// <param name="kind">Which kind of condition changed.</param>
    /// <param name="subject">Which thing of that kind — used for the mail's subject/body formatting.</param>
    /// <param name="auditSubject">
    /// What is written to the journal for this transition. Equal to <paramref name="subject"/> for
    /// every existing kind; for a drifted code-integrity report it additionally carries the
    /// installed version, the differing-file count, and a capped path list in <c>key=value</c> form
    /// (see <see cref="Mappers.AlertDetailMapper.FormatDifferingPathsAuditSubject"/>) — the journal outlives the locale the
    /// writer happened to be using, so this is never a localized sentence.
    /// </param>
    /// <param name="transition">Whether the episode opened or closed.</param>
    /// <param name="detail">The figure or the service manager's words that go in the body.</param>
    /// <param name="cancellationToken">Cancels the journal writes and the recipient read.</param>
    /// <remarks>
    /// The words are composed here; journalling the transition, finding the recipient, requesting
    /// the mail and journalling <c>MailSkippedNoSmtp</c> when there is nobody to tell are
    /// <see cref="OperatorAlertMailer"/>'s, the one path the Backups module's alerts take too — its
    /// remarks carry why the journal entry comes first and why a skipped mail is a success.
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
        var subjectLine = _text[SubjectKey(kind, transition), subject].Value;
        var body = _text[BodyKey(kind, transition), subject, detail].Value;

        await _operatorAlerts.AlertAsync(
            _journal, action, $"{kind}:{auditSubject}", subjectLine, body, cancellationToken);
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
