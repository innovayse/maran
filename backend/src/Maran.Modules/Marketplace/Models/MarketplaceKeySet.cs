using Maran.Modules.Marketplace.Interfaces;

namespace Maran.Modules.Marketplace.Models;

/// <summary>The service's list of signing keys. Trusted only when a key the panel already trusts signed it.</summary>
/// <param name="Keys">The keys the service announces.</param>
/// <param name="SignedAt">When the service signed the list.</param>
/// <param name="ValidUntil">The last moment the service stands behind the list.</param>
public sealed record MarketplaceKeySet(
    IReadOnlyList<MarketplaceKeyEntry> Keys,
    DateTimeOffset SignedAt,
    DateTimeOffset ValidUntil) : ISignedMarketplaceDocument;
