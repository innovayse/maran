namespace Maran.Modules.ServerMigration.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources are reached
/// through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the ServerMigration module owns: the
/// module's display name, its menu label and route title, and one sentence per kind of discovery warning
/// (key <c>DiscoveryWarning</c> followed by the kind's name).
/// </summary>
public sealed class DisplayNames
{
}
