namespace Maran.Modules.Tasks.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the Tasks
/// module owns: <c>TasksModuleDisplayName</c> (resolved via <see cref="TasksManifest"/>'s
/// <c>DisplayNameKey</c>) and one <c>TaskKind&lt;Kind&gt;</c> entry per <c>TaskKinds</c> constant.
/// </summary>
public sealed class DisplayNames
{
}
