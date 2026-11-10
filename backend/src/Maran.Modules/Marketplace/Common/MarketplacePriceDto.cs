namespace Maran.Modules.Marketplace.Common;

/// <summary>A module's price as the service states it. Display only; a purchase happens in the vendor's cabinet.</summary>
/// <param name="Display">The price text, localized by the service.</param>
/// <param name="PurchaseUrl">Where to buy the module, only when it is an https address; otherwise null.</param>
public sealed record MarketplacePriceDto(string Display, string? PurchaseUrl);
