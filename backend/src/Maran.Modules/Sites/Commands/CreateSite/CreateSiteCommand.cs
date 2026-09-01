using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Commands.CreateSite;

/// <summary>
/// Creates a website under an account: its document root, its nginx vhost and — for a PHP backend —
/// its FPM pool, through the agent, and then the row that defines it (spec §11).
/// </summary>
/// <param name="AccountId">The account that will own the site.</param>
/// <param name="Domain">The primary domain the site serves.</param>
/// <param name="Aliases">Additional hostnames answered by the same vhost.</param>
/// <param name="BackendType">Which backend serves the site's content.</param>
/// <param name="PhpVersion">The installed PHP version to bind to; required when the backend is PHP.</param>
/// <param name="ProxyUpstream">The upstream to forward to; required when the backend is a reverse proxy.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record CreateSiteCommand(
    Guid AccountId,
    string Domain,
    IReadOnlyList<string> Aliases,
    SiteBackendType BackendType,
    string PhpVersion,
    string ProxyUpstream,
    string IpAddress,
    string UserAgent);
