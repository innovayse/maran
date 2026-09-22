namespace Maran.Modules.Sites.Controllers.Requests;

/// <summary>The body of <c>POST /api/v1/sites/{id}/php-version</c>.</summary>
/// <param name="PhpVersion">The installed version to switch to.</param>
public sealed record ChangeSitePhpVersionRequest(string PhpVersion);
