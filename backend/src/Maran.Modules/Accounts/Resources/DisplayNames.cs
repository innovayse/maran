namespace Maran.Modules.Accounts.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the
/// Accounts module owns: <c>AccountsModuleDisplayName</c> (resolved via <see cref="AccountsManifest"/>'s
/// <c>DisplayNameKey</c>) and each seeded plan's name (<c>PlanStarterName</c>,
/// <c>PlanBusinessName</c>, <c>PlanUnlimitedName</c>).
/// Also carries the labels of the module's menu entries (<c>AccountsMenu*</c>) and the titles of its routes
/// (<c>AccountsRoute*</c>),
/// resolved through <see cref="AccountsManifest"/>'s menu and route lists.
/// </summary>
public sealed class DisplayNames
{
}
