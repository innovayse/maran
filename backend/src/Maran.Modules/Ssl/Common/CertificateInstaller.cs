using Maran.Agent.Client.Interfaces;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Common;

/// <summary>
/// Puts certificate material onto the host and records, on the site, that the site now serves TLS.
/// The one code path issuance, customer-supplied installation and unattended renewal all go through.
/// </summary>
/// <remarks>
/// The two steps are one operation and must not be separated, which is why they live here rather than
/// in three handlers. The agent writes the material and rewrites the vhost; the site row's
/// <c>HasCertificate</c> flag is what every LATER re-render of that vhost is told. Perform the first
/// without the second and the site serves TLS until the next unrelated edit — a PHP version change,
/// an alias added — re-renders the vhost from a row that still says "no certificate" and drops a live
/// site back to plain HTTP.
///
/// Order matters and is deliberate: the agent runs FIRST and the flag is set only if it succeeded. A
/// flag set for a certificate that was never installed would make the next re-render write a TLS
/// block pointing at files that do not exist, which nginx refuses to load — taking the site down
/// rather than merely leaving it on HTTP.
///
/// No parameter here is ever logged. The material passes through as arguments and is dropped
/// (rules/security.md item 8).
/// </remarks>
public sealed class CertificateInstaller
{
    /// <summary>The agent, which owns the certificate store and the vhost.</summary>
    private readonly IAgentSslClient _agent;

    /// <summary>The one window onto the Sites module, and the only hand on its certificate flag.</summary>
    private readonly ISiteDirectory _sites;

    /// <summary>Creates the installer.</summary>
    /// <param name="agent">The agent client that writes the material and rewrites the vhost.</param>
    /// <param name="sites">The Sdk abstraction over the module that owns site rows.</param>
    public CertificateInstaller(IAgentSslClient agent, ISiteDirectory sites)
    {
        _agent = agent;
        _sites = sites;
    }

    /// <summary>Installs material for a site and marks the site as carrying a certificate.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="site">The site's facts, so the rewritten vhost is still the same site.</param>
    /// <param name="certificatePem">PEM-encoded leaf certificate, optionally followed by its chain.</param>
    /// <param name="privateKeyPem">PEM-encoded private key matching the certificate. Never logged.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>When the installed certificate expires, or the agent's own typed failure.</returns>
    public async Task<Result<DateTimeOffset>> InstallAsync(
        string accountUsername,
        SiteSnapshot site,
        string certificatePem,
        string privateKeyPem,
        CancellationToken cancellationToken)
    {
        var installed = await _agent.InstallCertificateAsync(
            accountUsername,
            site.Domain,
            certificatePem,
            privateKeyPem,
            SiteDescriptorFactory.From(site, hasCertificate: true),
            cancellationToken);
        if (!installed.IsSuccess)
        {
            return Result<DateTimeOffset>.Fail(installed.Error!);
        }

        await _sites.AttachCertificateAsync(site.Id, cancellationToken);

        return Result<DateTimeOffset>.Ok(installed.Value.ExpiresAt);
    }
}
