namespace Maran.Modules.Marketplace.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources are
/// reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries the module's own name
/// (<c>MarketplaceModuleDisplayName</c>, resolved through <see cref="MarketplaceManifest"/>'s
/// <c>DisplayNameKey</c>), the localized lines that state what the panel sends to the marketplace
/// (<c>SentItem*</c>), and the names of the entitlement states a module can be in (<c>Entitlement*</c>).
/// </summary>
public sealed class DisplayNames
{
}
