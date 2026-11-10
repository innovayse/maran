using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Models;

/// <summary>One site of an account as it travels to another server.</summary>
/// <param name="Domain">The site's primary domain.</param>
/// <param name="Aliases">The other hostnames the site answers to.</param>
/// <param name="BackendType">Static files, PHP, a reverse proxy or a Node application.</param>
/// <param name="PhpVersion">The PHP version for a PHP site, otherwise empty.</param>
/// <param name="ProxyUpstream">The upstream of a reverse-proxy site, otherwise empty.</param>
/// <param name="Enabled">Whether the site was serving.</param>
/// <param name="NodeVersion">The Node major for a Node site, otherwise empty. Absent in sections exported before Node sites existed.</param>
/// <param name="AppPort">The loopback port of a Node site, otherwise zero. Absent in sections exported before Node sites existed.</param>
/// <param name="StartCommand">The start command of a Node site, otherwise empty. Absent in sections exported before Node sites existed.</param>
public sealed record SiteMigrationItem(
    string Domain,
    IReadOnlyList<string> Aliases,
    SiteBackendType BackendType,
    string PhpVersion,
    string ProxyUpstream,
    bool Enabled,
    string NodeVersion = "",
    int AppPort = 0,
    string StartCommand = "");
