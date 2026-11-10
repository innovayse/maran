namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// A version the administrator asked to install or update, found in the verified catalogue, with the
/// panel-side facts it is judged against.
/// </summary>
/// <param name="Module">The catalogue module.</param>
/// <param name="Version">The version asked for, or null when the catalogue does not offer it.</param>
/// <param name="Context">The panel-side facts, read once.</param>
public sealed record ResolvedOffer(CatalogueModule Module, CatalogueVersion? Version, EligibilityContext Context);
