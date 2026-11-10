using Maran.Modules.Marketplace.Interfaces;

namespace Maran.Modules.Marketplace.Models;

/// <summary>One module with every version the service offers this panel, and the manifest summary of the newest.</summary>
/// <param name="Module">The module as the catalogue lists it.</param>
/// <param name="Versions">Every version compatible with this panel, newest first.</param>
/// <param name="SignedAt">When the service signed the answer.</param>
/// <param name="ValidUntil">The last moment the service stands behind the answer.</param>
/// <param name="Manifest">The manifest summary of the newest version, or null when there is none to show.</param>
public sealed record CatalogueModuleDetails(
    CatalogueModule Module,
    IReadOnlyList<CatalogueVersion> Versions,
    DateTimeOffset SignedAt,
    DateTimeOffset ValidUntil,
    CatalogueManifestSummary? Manifest = null) : ISignedMarketplaceDocument;
