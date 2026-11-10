namespace Maran.Modules.Marketplace.Common;

/// <summary>One published version of a module.</summary>
/// <param name="Version">The version.</param>
/// <param name="MinPanelVersion">The oldest panel version it runs on.</param>
/// <param name="MaxPanelVersion">The newest panel version it runs on, or null for no limit.</param>
/// <param name="SizeBytes">The package size.</param>
/// <param name="PublishedAt">When it was published.</param>
/// <param name="ReleaseNotes">The release notes, plain text.</param>
/// <param name="Revoked">Whether the service has withdrawn this version.</param>
/// <param name="RevokedMessage">Why it was withdrawn, or null.</param>
/// <param name="Installable">Whether this version can be installed now; decided by the server.</param>
/// <param name="NotInstallableMessage">Why not, in the caller's language; null when installable.</param>
public sealed record MarketplaceVersionDto(
    string Version,
    string MinPanelVersion,
    string? MaxPanelVersion,
    long SizeBytes,
    DateTimeOffset PublishedAt,
    string ReleaseNotes,
    bool Revoked,
    string? RevokedMessage,
    bool Installable,
    string? NotInstallableMessage);
