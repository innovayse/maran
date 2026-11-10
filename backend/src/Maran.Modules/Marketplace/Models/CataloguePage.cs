using Maran.Modules.Marketplace.Interfaces;

namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// One page of the catalogue. A page exists only after its signature and freshness were verified: an
/// answer that did not verify is a coded failure, never a page, so holding one is the proof.
/// </summary>
/// <param name="Items">The modules on this page.</param>
/// <param name="SignedAt">When the service signed the page.</param>
/// <param name="ValidUntil">The last moment the service stands behind the page.</param>
/// <param name="NextCursor">The cursor of the next page, or null on the last one.</param>
public sealed record CataloguePage(
    IReadOnlyList<CatalogueModule> Items,
    DateTimeOffset SignedAt,
    DateTimeOffset ValidUntil,
    string? NextCursor = null) : ISignedMarketplaceDocument;
