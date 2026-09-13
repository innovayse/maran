namespace Maran.Modules.Notifications.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/NotificationMessages.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries the subjects and bodies of the
/// mail this module sends on its own initiative: the alert raised and resolved pairs, and the test
/// message.
/// </summary>
/// <remarks>
/// Its own file rather than a second purpose bolted onto <c>ErrorMessages</c>, because the two are
/// read by different things and travel differently: an error code becomes an RFC 7807 payload in the
/// caller's own culture, while these become the body of a mail nobody is waiting on. One resource
/// file per purpose (rules/csharp.md).
/// </remarks>
public sealed class NotificationMessages
{
}
