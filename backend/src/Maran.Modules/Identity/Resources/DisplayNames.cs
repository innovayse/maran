namespace Maran.Modules.Identity.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>. Carries the module's own
/// display name, resolved for the modules catalogue through
/// <see cref="Maran.Sdk.Contracts.Manifest.DisplayNameKey"/>.
/// Also carries the labels of the module's menu entries (<c>IdentityMenu*</c>) and the titles of its routes
/// (<c>IdentityRoute*</c>),
/// resolved through <see cref="IdentityManifest"/>'s menu and route lists.
/// </summary>
public sealed class DisplayNames
{
}
