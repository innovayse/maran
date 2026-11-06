using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Jobs;

/// <summary>
/// Offers one certificate to the operator's expiry alert on behalf of a renewal pass, so that a mail
/// that cannot be raised never costs the pass a renewal.
/// </summary>
/// <remarks>
/// A static helper beside the handler rather than a registered service: it holds no state and has no
/// lifetime, and it exists only to keep the handler to one unit's worth of file. The alert is a second
/// line of defence behind renewal, so its failure is logged once and swallowed; the certificate's
/// alert mark is cleared again, because the mailer sets it before publishing and a mark left behind
/// by an alert that never went out would silence every later pass.
/// </remarks>
internal static class ExpiryAlertAttempt
{
    /// <summary>Pre-compiled log delegate for an alert that could not be raised.</summary>
    private static readonly Action<ILogger, string, Exception?> LogAlertFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(4, nameof(ExpiryAlertAttempt)),
            "The expiry alert for {Domain} could not be raised; the renewal pass continues and retries it next time");

    /// <summary>Asks the mailer to alert for a certificate, never letting it throw out of the pass.</summary>
    /// <param name="alerts">The operator's expiry alert.</param>
    /// <param name="certificate">The certificate the pass has just examined.</param>
    /// <param name="now">The instant the pass is working at.</param>
    /// <param name="logger">Where the one failure line is written. It carries the domain, never a secret.</param>
    /// <param name="cancellationToken">Cancels the alert; a cancellation is not swallowed.</param>
    /// <returns>A task that completes when the alert was raised, skipped or has failed and been logged.</returns>
    public static async Task TryAsync(
        ISslAlertMailer alerts,
        Certificate certificate,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await alerts.NotifyIfDueAsync(certificate, now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            certificate.ClearExpiryAlert();
            LogAlertFailed(logger, certificate.Domain, exception);
        }
    }
}
