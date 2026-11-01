namespace Maran.Modules.Backups.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/NotificationMessages.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries the subjects and bodies of the
/// operator alerts this module asks the Notifications module to send: a scheduled run that failed,
/// one whose destination refused it, and one that started late enough to have missed its hour.
/// </summary>
/// <remarks>
/// Its own file rather than more keys in <c>DisplayNames</c>, because the two are read differently:
/// a display name is a short phrase a screen shows, these are whole mails nobody is waiting on. One
/// resource file per purpose (rules/csharp.md).
/// </remarks>
public sealed class NotificationMessages
{
}
