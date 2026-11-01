using Maran.Modules.Ssl.Models;

namespace Maran.Modules.Ssl.Interfaces;

/// <summary>
/// Places and removes the file that answers an ACME HTTP-01 challenge.
/// </summary>
/// <remarks>
/// It is a seam for the same reason <see cref="IAcmeClient"/> is, and it is a SEPARATE seam because
/// the two talk to different things: the ACME client talks to an authority over HTTPS, and this
/// talks to the agent over a unix socket. A test that fakes issuance still wants the real question
/// answered — was the token written where the vhost serves it, and was it removed afterwards.
/// </remarks>
public interface IAcmeChallengeWriter
{
    /// <summary>
    /// Writes one challenge token where <paramref name="target"/> says: a site's document root, as the
    /// owning account, or the panel's own challenge directory.
    /// </summary>
    /// <param name="target">Where the file is placed.</param>
    /// <param name="domain">The domain being validated; for a site, its document root is where the file goes.</param>
    /// <param name="token">The challenge token, which is also the file's name.</param>
    /// <param name="keyAuthorization">The file's content: the token joined to the account key's thumbprint.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// Success; the agent's free-space floor refusal as itself (#148), since a disk nearly full is
    /// not a fault to re-word; or <c>AcmeChallengeWriteFailed</c> (a site) / <c>PanelChallengeWriteFailed</c> (the
    /// panel) for every other failure.
    /// </returns>
    Task<Result<bool>> WriteAsync(
        AcmeChallengeTarget target,
        string domain,
        string token,
        string keyAuthorization,
        CancellationToken cancellationToken);

    /// <summary>Removes a challenge token once the authority has read it, or once the order failed.</summary>
    /// <param name="target">Where the file was placed.</param>
    /// <param name="domain">The domain that was being validated.</param>
    /// <param name="token">The challenge token, which is also the file's name.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or the agent's own typed failure — including a file that is already gone.</returns>
    /// <remarks>
    /// Best-effort by contract: the caller MUST NOT fail an otherwise-successful issuance because the
    /// cleanup failed. A stale token under <c>.well-known</c> proves nothing to anybody — it is valid
    /// only against an authorization that has already been consumed — whereas discarding an issued
    /// certificate over a failed unlink would be a real loss.
    /// </remarks>
    Task<Result<bool>> RemoveAsync(
        AcmeChallengeTarget target,
        string domain,
        string token,
        CancellationToken cancellationToken);
}
