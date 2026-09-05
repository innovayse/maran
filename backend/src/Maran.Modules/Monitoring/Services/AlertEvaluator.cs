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
    /// <param name="observedAt">When the readings were taken, from the panel's clock.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    public async Task EvaluateAsync(
        double? diskUsedPercent,
        IReadOnlyList<AgentServiceStatus> services,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var pending = new List<(AlertKind Kind, string Subject, AlertTransition Transition, string Detail)>();

        if (diskUsedPercent is not null)
        {
            var transition = await ObserveAsync(
                AlertKind.DiskUsage,
                RootFilesystemSubject,
                diskUsedPercent.Value > DiskUsageThresholdPercent,
                observedAt,
                cancellationToken);

            pending.Add((AlertKind.DiskUsage, RootFilesystemSubject, transition, FormatPercent(diskUsedPercent.Value)));
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

            pending.Add((AlertKind.ServiceStopped, name, transition, service.Detail));
        }

        // One save for the whole round, before any mail is attempted. See the type's remarks: a
        // transition that was not committed is a transition that repeats on the next sample.
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var (kind, subject, transition, detail) in pending)
        {
            if (transition == AlertTransition.None)
            {
                continue;
            }

            await AnnounceAsync(kind, subject, transition, detail, cancellationToken);
        }
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
    /// <param name="subject">Which thing of that kind.</param>
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
        AlertTransition transition,
        string detail,
        CancellationToken cancellationToken)
    {
        var action = transition == AlertTransition.Raised ? AuditActions.AlertRaised : AuditActions.AlertResolved;
        await _journal.RecordSystemAsync(action, $"{kind}:{subject}", succeeded: true, cancellationToken);

        var recipient = await _recipients.GetAlertRecipientAsync(cancellationToken);
        if (recipient is null)
        {
            await _journal.RecordSystemAsync(
                AuditActions.MailSkippedNoSmtp, $"{kind}:{subject}", succeeded: false, cancellationToken);
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
