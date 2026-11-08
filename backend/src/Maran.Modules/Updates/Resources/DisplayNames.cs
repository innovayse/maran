namespace Maran.Modules.Updates.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries the one name the Updates module
/// owns, <c>UpdatesModuleDisplayName</c>, resolved through <see cref="UpdatesManifest"/>'s
/// <c>DisplayNameKey</c>.
/// </summary>
public sealed class DisplayNames
{
}
