using System.Globalization;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Domain.Policies;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Tells the administrator that a certificate is close to expiring, so a renewal that keeps failing
/// is noticed before the site goes dark (issue #161).
/// </summary>
/// <remarks>
/// <para>
/// <b>It owns the words and the decision; the path is shared.</b> Only this module knows what the
/// mail is about and when it is due. Journalling the alert, finding where operator alerts go and
/// publishing the mail are <see cref="OperatorAlertDispatcher"/>'s, the one implementation Monitoring
/// and Backups call too. The entries are written through this module's own journal.
/// </para>
/// <para>
/// <b>One alert per material.</b> The certificate carries <see cref="Certificate.ExpiryAlertedAt"/>;
/// this type reads it, raises the alert only while it is empty and marks the certificate. The
/// renewal pass that replaces the material clears the mark, so the next certificate is announced
/// afresh. Saving the mark is the caller's, in the same save as the rest of its outcome.
/// </para>
/// <para>
/// <b>The mail says what and when, never where or how.</b> It names the domain, the days left, the
/// expiry date and, when a renewal has failed, the failure's localized sentence. It carries no path,
/// no certificate material and no tool output (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class SslAlertMailer : ISslAlertMailer
{
    /// <summary>Resource key of the alert's subject.</summary>
    private const string ExpirySubject = "CertificateExpiryAlertSubject";

    /// <summary>Resource key of the alert's body.</summary>
    private const string ExpiryBody = "CertificateExpiryAlertBody";

    /// <summary>Resource key of the sentence appended to the body when a renewal has failed.</summary>
    private const string ExpiryLastFailure = "CertificateExpiryAlertLastFailure";

    /// <summary>How the expiry instant is written into a mail: ISO 8601 date and minute, always UTC.</summary>
    private const string InstantFormat = "yyyy-MM-dd HH:mm";

    /// <summary>The audit subject's prefix, naming what the alert reports.</summary>
    private const string AuditSubjectKind = "CertificateExpiry";

    /// <summary>The panel's one path for an operator alert: journal, recipient, mail.</summary>
    private readonly OperatorAlertDispatcher _operatorAlerts;

    /// <summary>This module's audit journal.</summary>
    private readonly CertificateAuditJournal _journal;

    /// <summary>The subjects and bodies of the alert mail, in the panel's languages.</summary>
    private readonly IStringLocalizer<NotificationMessages> _text;

    /// <summary>Describes a stored renewal failure as a sentence.</summary>
    private readonly CertificateRenewalFailureMessages _failures;

    /// <summary>Creates the mailer.</summary>
    /// <param name="operatorAlerts">The panel's one path for an operator alert.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="text">The localized subjects and bodies of the alert mail.</param>
    /// <param name="failures">Describes a stored renewal failure as a sentence.</param>
    public SslAlertMailer(
        OperatorAlertDispatcher operatorAlerts,
        CertificateAuditJournal journal,
        IStringLocalizer<NotificationMessages> text,
        CertificateRenewalFailureMessages failures)
    {
        _operatorAlerts = operatorAlerts;
        _journal = journal;
        _text = text;
        _failures = failures;
    }

    /// <inheritdoc />
    public async Task<bool> NotifyIfDueAsync(
        Certificate certificate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (certificate.ExpiryAlertedAt is not null || !CertificateExpiryAlertPolicy.IsDue(certificate.NotAfter, now))
        {
            return false;
        }

        var daysLeft = Math.Max(0, (int)Math.Floor((certificate.NotAfter - now).TotalDays));
        var body = _text[
            ExpiryBody,
            certificate.Domain,
            daysLeft.ToString(CultureInfo.InvariantCulture),
            certificate.NotAfter.ToUniversalTime().ToString(InstantFormat, CultureInfo.InvariantCulture)].Value;

        var failure = _failures.Describe(certificate.LastRenewalFailureCode);
        if (failure is not null)
        {
            body = $"{body} {_text[ExpiryLastFailure, failure].Value}";
        }

        var alert = new OperatorAlert(
            AlertSource.Ssl,
            AuditSubjectKind,
            AlertTransition.Occurred,
            certificate.Domain,
            $"daysLeft={daysLeft} notAfter={certificate.NotAfter.ToUniversalTime().ToString(InstantFormat, CultureInfo.InvariantCulture)}");

        await _operatorAlerts.AlertAsync(
            _journal,
            MonitoringAuditActions.AlertRaised,
            $"{AuditSubjectKind}:{certificate.Domain}",
            alert,
            _text[ExpirySubject, certificate.Domain].Value,
            body,
            cancellationToken);

        certificate.MarkExpiryAlerted(now);
        return true;
    }
}
