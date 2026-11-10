namespace Maran.Modules.ServerMigration.Models;

/// <summary>One site of the comparison report: what this panel's own web server answered for it.</summary>
/// <param name="Domain">The site's host name.</param>
/// <param name="StatusCode">The HTTP status, or <c>null</c> when the web server did not answer.</param>
/// <param name="Title">The page title, or <c>null</c>.</param>
/// <param name="Serves">Whether the status is a success or a redirect.</param>
public sealed record MigrationSiteReport(string Domain, int? StatusCode, string? Title, bool Serves);
