namespace Maran.Modules.Firewall.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the
/// Firewall module owns: <c>FirewallModuleDisplayName</c> (resolved through the manifest's
/// <c>DisplayNameKey</c>) and one <c>FirewallProtocol&lt;Member&gt;</c> entry per transport protocol
/// the rule form offers.
/// </summary>
public sealed class DisplayNames
{
}
