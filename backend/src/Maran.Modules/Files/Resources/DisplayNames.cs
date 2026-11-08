namespace Maran.Modules.Files.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the Files
/// module owns: <c>FilesModuleDisplayName</c> (resolved via <see cref="FilesManifest"/>'s
/// <c>DisplayNameKey</c>) and the names of an entry's kinds.
/// Also carries the labels of the module's menu entries (<c>FilesMenu*</c>) and the titles of its routes
/// (<c>FilesRoute*</c>),
/// resolved through <see cref="FilesManifest"/>'s menu and route lists.
/// </summary>
public sealed class DisplayNames
{
}
