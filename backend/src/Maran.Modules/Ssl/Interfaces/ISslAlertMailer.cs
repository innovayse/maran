using Maran.Modules.Ssl.Domain.Entities;

namespace Maran.Modules.Ssl.Interfaces;

/// <summary>
/// Tells the server's administrator that a certificate is about to expire, once per certificate
/// material, however many renewal passes find it still inside the alert window (issue #161).
/// </summary>
public interface ISslAlertMailer
{
    /// <summary>
    /// Raises the expiry alert for a certificate when it is due and has not been raised yet.
    /// </summary>
    /// <param name="certificate">The certificate just examined by a renewal pass.</param>
    /// <param name="now">The instant the pass is working at.</param>
    /// <param name="cancellationToken">Cancels the journal writes and the recipient read.</param>
    /// <returns>
    /// <c>true</c> when an alert was raised, in which case the certificate has been marked as alerted
    /// and the CALLER must save it; <c>false</c> when nothing was due or the alert was already raised.
    /// </returns>
    Task<bool> NotifyIfDueAsync(Certificate certificate, DateTimeOffset now, CancellationToken cancellationToken);
}
