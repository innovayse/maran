namespace Maran.Modules.Accounts.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the
/// Accounts module owns: <c>ModuleDisplayName</c> (resolved via <see cref="AccountsManifest"/>'s
/// <c>DisplayNameKey</c>) and each seeded plan's name (<c>PlanStarterName</c>,
/// <c>PlanBusinessName</c>, <c>PlanUnlimitedName</c>).
/// </summary>
public sealed class DisplayNames
{
}
