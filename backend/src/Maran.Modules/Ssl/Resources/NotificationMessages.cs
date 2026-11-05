namespace Maran.Modules.Ssl.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/NotificationMessages.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries the subject and body of the
/// operator alert this module asks the Notifications module to send when a certificate is close to
/// expiring.
/// </summary>
/// <remarks>
/// Its own file rather than more keys in <c>DisplayNames</c>, because the two are read differently:
/// a display name is a short phrase a screen shows, this is a whole mail nobody is waiting on. One
/// resource file per purpose (rules/csharp.md).
/// </remarks>
public sealed class NotificationMessages
{
}
