using Maran.Host.Demo.World.Enums;
using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Host.Demo.World;

/// <summary>One site of the demo world.</summary>
/// <param name="Domain">The site's primary host name.</param>
/// <param name="BackendType">How the site is served.</param>
/// <param name="PhpVersion">The PHP version of a PHP site, empty for any other backend.</param>
/// <param name="ProxyUpstream">The upstream of a reverse-proxy site, empty for any other backend.</param>
/// <param name="Aliases">Extra host names the site answers to.</param>
/// <param name="Certificate">The certificate state the seed leaves the site in.</param>
/// <param name="Enabled">Whether the site is serving; a disabled site is disabled after it is created.</param>
public sealed record DemoSiteDefinition(
    string Domain,
    SiteBackendType BackendType,
    string PhpVersion,
    string ProxyUpstream,
    IReadOnlyList<string> Aliases,
    DemoCertificateState Certificate,
    bool Enabled);
