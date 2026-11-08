namespace Maran.Modules.Fleet.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries the one name the Fleet module owns,
/// <c>FleetModuleDisplayName</c>, resolved through <see cref="FleetManifest"/>'s <c>DisplayNameKey</c>.
/// </summary>
public sealed class DisplayNames
{
}
