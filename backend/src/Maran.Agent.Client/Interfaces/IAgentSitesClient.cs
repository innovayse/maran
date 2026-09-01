using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the agent's site operations: the vhost, document root and PHP pool behind a
/// domain.
/// </summary>
/// <remarks>
/// Every method returns a <see cref="Result{T}"/> rather than throwing: a site that already exists,
/// or one the agent cannot find, is an answer the caller acts on, not an exception (rules/csharp.md
/// "Errors: Result, not exceptions"). The operations that re-render a vhost take a
/// <see cref="SiteDescriptor"/>, because the panel's database — not the file on disk — is what a
/// site is.
/// </remarks>
public interface IAgentSitesClient
{
    /// <summary>Creates the document root, renders and validates the vhost, and reloads the web server.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">Primary domain served by the site.</param>
    /// <param name="aliases">Additional hostnames served by the same site.</param>
    /// <param name="kind">Which backend serves the site's content.</param>
    /// <param name="phpVersion">Installed PHP version to bind to; required when the backend is PHP.</param>
    /// <param name="proxyUpstream">Upstream to forward to; required when the backend is a reverse proxy.</param>
    /// <param name="maxChildren">The plan's worker budget, written into the pool's <c>pm.max_children</c>; ignored unless the backend is PHP.</param>
    /// <param name="settingOverrides">The customer's php.ini overrides, re-validated by the agent; ignored unless the backend is PHP.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Where the site's files live, or a typed failure.</returns>
    Task<Result<CreatedSiteDto>> CreateAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<string> aliases,
        SiteBackendKind kind,
        string phpVersion,
        string proxyUpstream,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken);

    /// <summary>Rebinds a PHP-backed site to a different installed version and reloads its pool.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">Primary domain of the site to update.</param>
    /// <param name="phpVersion">The installed version to switch to.</param>
    /// <param name="site">What the site is, so its vhost re-renders to the text it already had.</param>
    /// <param name="maxChildren">The plan's worker budget, written into the pool's <c>pm.max_children</c>.</param>
    /// <param name="settingOverrides">The customer's php.ini overrides, re-validated by the agent.</param>
    /// <param name="removePreviousPool">
    /// Whether the pool of the version being left behind may be removed. True only when no other
    /// site of this account still uses that version, for the reason
    /// <see cref="DeleteAsync"/> gives.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result<bool>> ChangePhpVersionAsync(
        string accountUsername,
        string domain,
        string phpVersion,
        SiteDescriptor site,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        bool removePreviousPool,
        CancellationToken cancellationToken);

    /// <summary>Restores normal serving for a disabled site.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">Primary domain of the site to enable.</param>
    /// <param name="site">What the site is, so the restored vhost is the site's own again.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result<bool>> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken);

    /// <summary>Serves a suspension response instead of the site's content, keeping the vhost in place.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">Primary domain of the site to disable.</param>
    /// <param name="site">What the site is, so the suspended vhost keeps its aliases and log paths.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result<bool>> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken);

    /// <summary>Removes the site's vhost include and reloads the web server; its files are left alone.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">Primary domain of the site to delete.</param>
    /// <param name="retiredPhpVersion">
    /// The PHP version whose pool may go with the site, or the empty string to leave every pool
    /// alone. Only the caller can decide: a php-fpm pool belongs to an ACCOUNT and a version, and
    /// two of the account's sites on the same version share one pool and one worker budget — so
    /// this is set only when no other site of this account still uses the version.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        CancellationToken cancellationToken);

    /// <summary>Tails one of a site's logs: recent lines first, then new ones as they are written.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">Primary domain of the site whose log is read.</param>
    /// <param name="logSource">Which log to tail.</param>
    /// <param name="historyLines">How many historical lines to replay first; the agent caps this.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// The lines, followed by exactly one terminal event naming how the stream ended — completed,
    /// dropped, idle, failed, or cancelled by the caller. The sequence never ends without one, so a
    /// caller cannot mistake a dropped, idle or abandoned stream for a log that simply had nothing
    /// more to say.
    /// </returns>
    IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        CancellationToken cancellationToken);

    /// <summary>Validates the running web-server configuration and reloads it once.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure; on a validation failure the previous config keeps running.</returns>
    /// <remarks>
    /// The batch-reload path, and the reason it takes no site: a renewal pass touches every site whose
    /// certificate is near expiry, and reloading once at the end costs one reload instead of one per
    /// site. Server-wide by nature — the web server has a single configuration — so it carries no
    /// account name and no domain, and a caller must have already established that it may act on every
    /// site it changed. Idempotent: reloading with nothing pending is a no-op success.
    /// </remarks>
    Task<Result<bool>> ReloadWebServerAsync(CancellationToken cancellationToken);
}
