namespace Maran.Modules.ServerMigration.Common;

/// <summary>What this panel's own web server answered for one migrated site.</summary>
/// <param name="Domain">The site's host name.</param>
/// <param name="StatusCode">The HTTP status, or <c>null</c> when it did not answer.</param>
/// <param name="Title">The page title.</param>
/// <param name="Serves">Whether the status is a success or a redirect.</param>
public sealed record MigrationReportSiteDto(string Domain, int? StatusCode, string? Title, bool Serves);
