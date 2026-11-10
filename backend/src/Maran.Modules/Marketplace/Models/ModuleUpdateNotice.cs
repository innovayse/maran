namespace Maran.Modules.Marketplace.Models;

/// <summary>What the service says about one installed module: a newer version, a revocation, or both.</summary>
/// <param name="ModuleId">The installed module's id.</param>
/// <param name="Revoked">Whether the installed version has been withdrawn.</param>
/// <param name="Latest">The newest version this panel can update to, or null when there is none.</param>
/// <param name="RevokedReason">The service's localised reason when <paramref name="Revoked"/> is true.</param>
public sealed record ModuleUpdateNotice(
    string ModuleId,
    bool Revoked,
    CatalogueVersion? Latest = null,
    string? RevokedReason = null);
