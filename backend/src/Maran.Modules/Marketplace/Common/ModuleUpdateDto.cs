namespace Maran.Modules.Marketplace.Common;

/// <summary>What an update check found for one installed module.</summary>
/// <param name="Id">The module id.</param>
/// <param name="DisplayName">The name shown; the module id.</param>
/// <param name="InstalledVersion">The installed version.</param>
/// <param name="AvailableVersion">The newest version offered, or null when none is.</param>
/// <param name="Revoked">Whether the service has withdrawn the module.</param>
/// <param name="RevokedMessage">Why, or null.</param>
/// <param name="CanUpdate">Whether the update can be started now; decided by the server.</param>
/// <param name="CannotUpdateMessage">Why not, in the caller's language; null when <paramref name="CanUpdate"/> is true.</param>
public sealed record ModuleUpdateDto(
    string Id,
    string DisplayName,
    string InstalledVersion,
    string? AvailableVersion,
    bool Revoked,
    string? RevokedMessage,
    bool CanUpdate,
    string? CannotUpdateMessage);
