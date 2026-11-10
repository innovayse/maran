namespace Maran.Modules.Marketplace.Common;

/// <summary>What <c>GET /api/v1/marketplace/modules/{id}</c> answers.</summary>
/// <param name="Module">The module as the catalogue list shows it.</param>
/// <param name="Versions">Every version the service offers for this panel, newest first as the service orders them.</param>
/// <param name="Manifest">The signed manifest summary, or null when the service sent none.</param>
public sealed record MarketplaceModuleDetailDto(
    MarketplaceModuleDto Module,
    IReadOnlyList<MarketplaceVersionDto> Versions,
    ModuleManifestSummaryDto? Manifest);
