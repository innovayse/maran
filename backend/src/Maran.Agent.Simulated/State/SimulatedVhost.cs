using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;

namespace Maran.Agent.Simulated.State;

/// <summary>One site's web-server presence on the simulated host: its vhost and document root.</summary>
/// <param name="AccountUsername">The owning account's system user name.</param>
/// <param name="Domain">The primary domain; the site's key.</param>
/// <param name="Aliases">Additional host names the same vhost serves.</param>
/// <param name="Backend">Which backend serves the content.</param>
/// <param name="PhpVersion">The bound PHP version, or the empty string when the backend is not PHP.</param>
/// <param name="ProxyUpstream">The upstream forwarded to, or the empty string when not a reverse proxy.</param>
/// <param name="MaxChildren">The worker budget of the site's pool; zero for a site with no pool.</param>
/// <param name="SettingOverrides">The php.ini overrides, in the order they were given.</param>
/// <param name="Enabled">False while the vhost serves the suspension page instead of the content.</param>
/// <param name="DocumentRoot">The directory the content is served from.</param>
/// <param name="RotatedLogs">How many rotated archives each of the site's two logs holds.</param>
public sealed record SimulatedVhost(
    string AccountUsername,
    string Domain,
    IReadOnlyList<string> Aliases,
    SiteBackendKind Backend,
    string PhpVersion,
    string ProxyUpstream,
    uint MaxChildren,
    IReadOnlyList<PhpSettingDto> SettingOverrides,
    bool Enabled,
    string DocumentRoot,
    uint RotatedLogs = 14);
