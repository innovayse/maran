namespace Maran.Modules.Monitoring.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the
/// Monitoring module owns: <c>MonitoringModuleDisplayName</c> (resolved via
/// <see cref="MonitoringManifest"/>'s <c>DisplayNameKey</c>) and one <c>Service&lt;Member&gt;</c>
/// entry per <see cref="Maran.Agent.Client.Services.MonitorService.AgentManagedService"/> member.
/// </summary>
public sealed class DisplayNames
{
}
