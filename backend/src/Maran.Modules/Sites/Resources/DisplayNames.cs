namespace Maran.Modules.Sites.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the Sites
/// module owns: <c>SitesModuleDisplayName</c>, resolved via <see cref="SitesManifest"/>'s
/// <c>DisplayNameKey</c>, and the labels of the closed sets the site screens select from
/// (<c>SiteBackendType&lt;Member&gt;</c>, <c>SiteStatus&lt;Member&gt;</c>).
/// Also carries the labels of the module's menu entries (<c>SitesMenu*</c>) and the titles of its routes
/// (<c>SitesRoute*</c>),
/// resolved through <see cref="SitesManifest"/>'s menu and route lists.
/// </summary>
public sealed class DisplayNames
{
}
