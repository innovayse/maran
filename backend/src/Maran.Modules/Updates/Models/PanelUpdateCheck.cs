namespace Maran.Modules.Updates.Models;

/// <summary>
/// What the root CLI's last signed-manifest fetch found, after the panel has re-checked every value.
/// A value that failed its check is carried as null, never as the text that failed.
/// </summary>
/// <param name="CheckedAt">When the root CLI fetched the manifest.</param>
/// <param name="Version">The release the manifest names, a Semantic Versioning string.</param>
/// <param name="ReleasedAt">The release's publication day as <c>yyyy-MM-dd</c>, or null when none was published or it was not plain.</param>
/// <param name="NotesUrl">The release notes address, or null when none was published or it was not a plain https address.</param>
public sealed record PanelUpdateCheck(DateTimeOffset CheckedAt, string Version, string? ReleasedAt, string? NotesUrl);
