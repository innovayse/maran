namespace Maran.Modules.Cron.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the Cron
/// module owns: <c>CronModuleDisplayName</c> (resolved via <see cref="CronManifest"/>'s
/// <c>DisplayNameKey</c>) and the names of the choices its screens select from — the schedule
/// builder's frequencies, the weekdays and the run outcomes.
/// </summary>
public sealed class DisplayNames
{
}
