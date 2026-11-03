namespace Maran.Modules.Notifications.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the
/// Notifications module owns: <c>NotificationsModuleDisplayName</c> (resolved through the manifest's
/// <c>DisplayNameKey</c>) and one <c>SmtpSecurity&lt;Mode&gt;</c> entry per
/// <c>SmtpSecurity</c> member, which is how the settings form is told what to call each choice.
/// </summary>
public sealed class DisplayNames
{
}
