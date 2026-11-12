using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Ssl.Mappers;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Services;

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
/// The whole operation runs under the site's operation lock (<see cref="ISiteOperationLock"/>), and the
/// site is READ AGAIN once the lock is held. The snapshot the caller passes was taken before the wait,
/// and a plan change, an alias edit or a PHP version change that finished during it would otherwise be
/// overwritten by a vhost rendered from facts that are no longer true.
///
/// What the lock and the re-read do NOT decide is whether the site is serving. A disabled site, and the site
/// of a suspended account, keep a stub vhost, and the snapshot carries no status on purpose: the agent
/// answers that question from the vhost on the host, by comparing the file with the stub rendered for this
/// descriptor, and it installs the material without rendering the site's own vhost when they are equal
/// (<c>ssl::install_certificate</c> and <c>ssl::remove_certificate</c>; their tests name the suspended case).
/// A status copied into the snapshot would be a second answer to the same question, read earlier than the
/// agent reads the file, so this module sends the site's own descriptor and lets the host refuse to serve it.
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

    /// <summary>What the app store has installed, which names the web server whose vhost is rewritten.</summary>
    private readonly IStackStatus _stack;

    /// <summary>The per-site lock every operation that rewrites the site's vhost holds.</summary>
    private readonly ISiteOperationLock _locks;

    /// <summary>Creates the installer.</summary>
    /// <param name="agent">The agent client that writes the material and rewrites the vhost.</param>
    /// <param name="sites">The Sdk abstraction over the module that owns site rows.</param>
    /// <param name="stack">What the app store has installed, which names the web server the vhost is rendered for.</param>
    /// <param name="locks">The per-site operation lock shared with the Sites module.</param>
    public CertificateInstaller(IAgentSslClient agent, ISiteDirectory sites, IStackStatus stack, ISiteOperationLock locks)
    {
        _agent = agent;
        _sites = sites;
        _stack = stack;
        _locks = locks;
    }

    /// <summary>Installs material for a site and marks the site as carrying a certificate.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="site">The site the material is for; its facts are re-read once the lock is held.</param>
    /// <param name="certificatePem">PEM-encoded leaf certificate, optionally followed by its chain.</param>
    /// <param name="privateKeyPem">PEM-encoded private key matching the certificate. Never logged.</param>
    /// <param name="lockWait">How long to wait for the site's lock before answering <c>SiteBusy</c>.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// When the installed certificate expires, <c>SiteBusy</c> when another operation held the site for
    /// the whole wait, <c>SiteNotFound</c> when the site went away meanwhile, or the agent's own typed failure.
    /// </returns>
    public async Task<Result<DateTimeOffset>> InstallAsync(
        string accountUsername,
        SiteSnapshot site,
        string certificatePem,
        string privateKeyPem,
        TimeSpan lockWait,
        CancellationToken cancellationToken)
    {
        var acquired = await _locks.AcquireAsync(site.Id, lockWait, cancellationToken);
        if (!acquired.IsSuccess)
        {
            return Result<DateTimeOffset>.Fail(acquired.Error!);
        }

        await using var held = acquired.Value;

        var current = await _sites.FindByIdUnscopedAsync(site.Id, cancellationToken);
        if (current is null)
        {
            return Result<DateTimeOffset>.Fail(Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound));
        }

        var installed = await _agent.InstallCertificateAsync(
            accountUsername,
            current.Domain,
            certificatePem,
            privateKeyPem,
            SiteDescriptorMapper.From(current, hasCertificate: true, SiteWebServerMapper.Require(_stack)),
            cancellationToken);
        if (!installed.IsSuccess)
        {
            return Result<DateTimeOffset>.Fail(installed.Error!);
        }

        await _sites.AttachCertificateAsync(current.Id, cancellationToken);

        return Result<DateTimeOffset>.Ok(installed.Value.ExpiresAt);
    }
}
