namespace Maran.Modules.Identity.Common;

/// <summary>
/// The one answer to "offer this existing login a place on my team". It is an echo of what the owner
/// typed, and it is the same whether or not the identifier named a login that could be offered, so
/// the form cannot be used to learn which login names or addresses exist on the server.
/// </summary>
/// <param name="Identifier">The login name or address exactly as the owner typed it.</param>
public sealed record MembershipOfferRequestedDto(string Identifier);
