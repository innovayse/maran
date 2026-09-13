namespace Maran.Modules.Identity.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/EmailTemplates.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>.
/// </summary>
/// <remarks>
/// A file of its own rather than entries in <c>ErrorMessages</c>, because one resource file per
/// purpose is the rule (rules/csharp.md) and these are the only strings this module composes into a
/// message rather than returning as an error code. The publisher renders them, in the recipient's
/// language, before the message is handed to the panel's mail queue — nothing downstream templates
/// or rewrites a body.
/// </remarks>
public sealed class EmailTemplates
{
}
