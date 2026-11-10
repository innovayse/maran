namespace Maran.Modules.Marketplace.Common;

/// <summary>What <c>GET /api/v1/marketplace/catalogue</c> answers.</summary>
/// <param name="Items">One page of modules; empty when the catalogue could not be verified.</param>
/// <param name="NextCursor">The cursor of the next page, or null on the last.</param>
/// <param name="Verified">Whether the answer's signature and freshness checked out. False is a 200, not an error, so the screen can still show the installed list.</param>
/// <param name="FetchedAt">When the answer was signed, or null when it was not verified.</param>
/// <param name="UnverifiedMessage">Why it was not verified, in the caller's language; only when <paramref name="Verified"/> is false.</param>
public sealed record MarketplaceCatalogueDto(
    IReadOnlyList<MarketplaceModuleDto> Items,
    string? NextCursor,
    bool Verified,
    DateTimeOffset? FetchedAt,
    string? UnverifiedMessage);
