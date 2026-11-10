namespace Maran.Modules.Marketplace.Models;

/// <summary>One module in the catalogue, as the service describes it for this panel.</summary>
/// <param name="Id">The module id.</param>
/// <param name="DisplayName">The name, already localised.</param>
/// <param name="Summary">One line about the module, already localised.</param>
/// <param name="Publisher">Who published it.</param>
/// <param name="Tier">The licence tier's wire name.</param>
/// <param name="Entitlement">Whether this panel's licence covers it.</param>
/// <param name="Revoked">Whether the service has withdrawn the module as a whole.</param>
/// <param name="Latest">The newest version this panel can install, or null when none is compatible.</param>
/// <param name="Price">The price, or null for a module that has none.</param>
public sealed record CatalogueModule(
    string Id,
    string DisplayName,
    string Summary,
    string Publisher,
    string Tier,
    CatalogueEntitlement Entitlement,
    bool Revoked,
    CatalogueVersion? Latest = null,
    CataloguePrice? Price = null);
