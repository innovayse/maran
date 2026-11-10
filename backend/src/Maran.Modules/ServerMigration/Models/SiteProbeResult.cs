namespace Maran.Modules.ServerMigration.Models;

/// <summary>What fetching a migrated site from this panel's own web server showed.</summary>
/// <param name="StatusCode">The HTTP status, or <c>null</c> when the web server did not answer.</param>
/// <param name="Title">The page's title, shortened and free of control characters, or <c>null</c> when it has none.</param>
public sealed record SiteProbeResult(int? StatusCode, string? Title);
