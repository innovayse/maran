namespace Maran.Modules.Sites.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the Sites
/// module owns: today just <c>SitesModuleDisplayName</c>, resolved via <see cref="SitesManifest"/>'s
/// <c>DisplayNameKey</c>.
/// </summary>
public sealed class DisplayNames
{
}
