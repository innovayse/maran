namespace Maran.Modules.Updates.Common;

/// <summary>A newer release the last check found, as the signed manifest describes it.</summary>
/// <param name="Version">The version the check found.</param>
/// <param name="ReleasedAt">When it was released, as a <c>yyyy-MM-dd</c> date, or null when the manifest carries none.</param>
/// <param name="NotesUrl">The release notes address (a plain https address), or null when none is published.</param>
public sealed record PanelUpdateAvailableDto(string Version, string? ReleasedAt, string? NotesUrl);
