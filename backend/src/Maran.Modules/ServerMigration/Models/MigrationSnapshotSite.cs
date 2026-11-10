using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Models;

/// <summary>One site of a source account, as the discovery found it when the job was made.</summary>
/// <param name="Domain">The primary host name.</param>
/// <param name="Aliases">The other host names the virtual host answers to.</param>
/// <param name="Backend">What serves the site.</param>
/// <param name="PhpVersion">The PHP version as <c>major.minor</c>, or <c>null</c>.</param>
public sealed record MigrationSnapshotSite(
    string Domain,
    IReadOnlyList<string> Aliases,
    DiscoveredSiteBackend Backend,
    string? PhpVersion);
