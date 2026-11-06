using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Domain.Policies;
using Maran.Modules.Ssl.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="ISslAlertMailer"/> double that records every offer and applies the same rule the real
/// mailer does: it raises an alert only inside the alert window and only once per certificate material.
/// </summary>
/// <remarks>
/// It marks the certificate exactly as the contract says, so a test can prove the pass saves the mark.
/// What it does not do is publish anything: the mail path is the real mailer's own tests' business.
/// </remarks>
public sealed class RecordingSslAlertMailer : ISslAlertMailer
{
    /// <summary>Every domain the mailer was offered, in order, whether or not it raised an alert.</summary>
    public List<string> Offered { get; } = [];

    /// <summary>The domains an alert was actually raised for, in order.</summary>
    public List<string> Raised { get; } = [];

    /// <summary>Whether the next offers throw instead of answering, as a failing mail path does.</summary>
    public bool Throws { get; set; }

    /// <inheritdoc />
    public Task<bool> NotifyIfDueAsync(Certificate certificate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Offered.Add(certificate.Domain);

        if (Throws)
        {
            // Marked first, as the real mailer marks before it publishes, so the test also proves the
            // pass undoes a mark left behind by an alert that never went out.
            certificate.MarkExpiryAlerted(now);
            throw new InvalidOperationException("The mail path is down.");
        }

        if (certificate.ExpiryAlertedAt is not null || !CertificateExpiryAlertPolicy.IsDue(certificate.NotAfter, now))
        {
            return Task.FromResult(false);
        }

        certificate.MarkExpiryAlerted(now);
        Raised.Add(certificate.Domain);
        return Task.FromResult(true);
    }
}
