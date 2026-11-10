namespace Maran.Modules.Marketplace.Models;

/// <summary>One signing key the service announces in its key list.</summary>
/// <param name="Id">The service's name for the key.</param>
/// <param name="PublicKeyPem">The key as a PEM <c>PUBLIC KEY</c> block; only an ECDSA P-384 key is accepted.</param>
public sealed record MarketplaceKeyEntry(string Id, string PublicKeyPem);
