namespace Maran.Modules.Backups.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the
/// Backups module owns: <c>BackupsModuleDisplayName</c> (resolved via <see cref="BackupsManifest"/>'s
/// <c>DisplayNameKey</c>), one <c>BackupFailure&lt;Code&gt;</c> entry per failure code a backup row
/// can record, and <c>BackupDestinationDefault</c>, the name of the one destination this panel
/// seeds for itself.
/// </summary>
public sealed class DisplayNames
{
}
