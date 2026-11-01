using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Services.SslService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the agent's TLS operations. Ordering a certificate is the panel's job; this
/// is only the placing of the material on the server and the wiring of it into the site.
/// </summary>
public interface IAgentSslClient
{
    /// <summary>Writes the certificate and key to the agent's store, wires them into the vhost and reloads.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">Domain the certificate is installed for.</param>
    /// <param name="certificatePem">PEM-encoded leaf certificate, optionally followed by its chain.</param>
    /// <param name="privateKeyPem">
    /// PEM-encoded private key matching the certificate. It is never logged: the client logs no
    /// request field, and PEM blocks are stripped from the agent's own error text before it is
    /// logged, so a key the agent quoted back cannot reach the panel's log either.
    /// </param>
    /// <param name="site">What the site is, so the TLS vhost matches the one the site would have had.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>When the installed certificate expires, or a typed failure.</returns>
    Task<Result<InstalledCertificateDto>> InstallCertificateAsync(
        string accountUsername,
        string domain,
        string certificatePem,
        string privateKeyPem,
        SiteDescriptor site,
        CancellationToken cancellationToken);

    /// <summary>Removes a domain's certificate files, reverts its vhost to plain HTTP, and reloads.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">Domain whose certificate is removed.</param>
    /// <param name="site">What the site is, so the plain-HTTP vhost it reverts to is the site's own.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when no certificate was installed.</returns>
    /// <remarks>
    /// Reverts to plain HTTP rather than to a self-signed placeholder, which is the agent's documented
    /// choice: a placeholder keeps 443 answering and gives every visitor a full-page interstitial, and
    /// where the real certificate had set HSTS, no click-through at all.
    /// </remarks>
    Task<Result<bool>> RemoveCertificateAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the panel's OWN certificate and key — the pair the panel's nginx vhost names —
    /// validates and reloads, restoring the previous pair on any refusal (issue #132).
    /// </summary>
    /// <param name="certificatePem">PEM-encoded leaf certificate, optionally followed by its chain.</param>
    /// <param name="privateKeyPem">PEM-encoded private key matching the certificate. Never logged.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>When the installed certificate expires, or a typed failure.</returns>
    Task<Result<InstalledCertificateDto>> InstallPanelCertificateAsync(
        string certificatePem,
        string privateKeyPem,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the HTTP-01 challenge file for the panel's own host name. Refused below the agent's
    /// free-space floor (<c>AgentBelowFreeSpaceFloor</c>) before anything is written.
    /// </summary>
    /// <param name="token">The challenge token, which names the file.</param>
    /// <param name="keyAuthorization">The file's content: the token joined to the account key's thumbprint.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result<bool>> WritePanelChallengeAsync(
        string token,
        string keyAuthorization,
        CancellationToken cancellationToken);

    /// <summary>Removes the panel's challenge file for a token.</summary>
    /// <param name="token">The challenge token whose file is removed.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when no file was in place.</returns>
    Task<Result<bool>> RemovePanelChallengeAsync(string token, CancellationToken cancellationToken);
}
