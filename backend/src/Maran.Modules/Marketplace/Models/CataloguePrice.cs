namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// A price as the service states it. Display only: a purchase happens in the vendor's own cabinet, never
/// in the panel, so nothing here is ever used to charge anyone.
/// </summary>
/// <param name="Display">The price as text, already localised, e.g. "9 EUR / month".</param>
/// <param name="PurchaseUrl">Where the administrator buys the module, or null. Shown only when it is https.</param>
/// <param name="Currency">The ISO currency code, or null.</param>
public sealed record CataloguePrice(string Display, string? PurchaseUrl = null, string? Currency = null);
