using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Common;

/// <summary>One site found under an account on a remote server.</summary>
/// <param name="Domain">The site's primary domain.</param>
/// <param name="Aliases">The other names the site answers to.</param>
/// <param name="Backend">How the site is served.</param>
/// <param name="BackendName">How the site is served, in the caller's language.</param>
/// <param name="PhpVersion">The PHP version it runs with, or <c>null</c> when it does not run PHP or it is unknown.</param>
/// <param name="DocumentRoot">The site's document root on the source.</param>
public sealed record DiscoveredSiteDto(
    string Domain,
    IReadOnlyList<string> Aliases,
    DiscoveredSiteBackend Backend,
    string BackendName,
    string? PhpVersion,
    string DocumentRoot);
