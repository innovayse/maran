namespace Maran.Modules.Marketplace.Models;

/// <summary>One published version of a module, with the facts the panel checks a download against.</summary>
/// <param name="Version">The semantic version.</param>
/// <param name="MinPanelVersion">The oldest panel version the module runs on.</param>
/// <param name="SizeBytes">The package file's size in bytes.</param>
/// <param name="Sha256">The package file's SHA-256, hexadecimal.</param>
/// <param name="PublishedAt">When the version was published.</param>
/// <param name="ReleaseNotes">The release notes, already localised.</param>
/// <param name="MaxPanelVersion">The newest panel version the module runs on, or null for no upper bound.</param>
/// <param name="Revoked">Whether the service has withdrawn this version.</param>
/// <param name="RevokedReason">The service's localised reason when <paramref name="Revoked"/> is true.</param>
public sealed record CatalogueVersion(
    string Version,
    string MinPanelVersion,
    long SizeBytes,
    string Sha256,
    DateTimeOffset PublishedAt,
    string ReleaseNotes,
    string? MaxPanelVersion = null,
    bool Revoked = false,
    string? RevokedReason = null);
