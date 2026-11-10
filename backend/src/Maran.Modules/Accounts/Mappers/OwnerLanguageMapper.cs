using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates a language code the panel ships as the name an operator reads. One place, because the
/// picker's list and the detail of a reseller both print the same name, and two lookups of one key
/// scheme are two chances for them to disagree.
/// </summary>
/// <remarks>
/// A language is named in itself in every culture's table (<c>English</c>, <c>Русский</c>,
/// <c>Հայերեն</c>), so the name reads the same in whichever language the administrator uses. A language
/// added to the panel's supported set without its three entries reads as its key; the list handler's
/// test asks for every language and catches it.
/// </remarks>
public static class OwnerLanguageMapper
{
    /// <summary>The prefix of a language's name in <c>DisplayNames</c>, followed by the upper-case code.</summary>
    private const string KeyPrefix = "OwnerLanguage";

    /// <summary>Names a language code.</summary>
    /// <param name="language">The language code on the wire, such as <c>en</c>.</param>
    /// <param name="displayNames">Resolves the name.</param>
    /// <returns>The language written in itself.</returns>
    public static string NameOf(string language, IStringLocalizer<DisplayNames> displayNames)
    {
        return displayNames[KeyPrefix + language.ToUpperInvariant()].Value;
    }

    /// <summary>Projects a language code onto the picker's wire shape.</summary>
    /// <param name="language">The language code on the wire, such as <c>en</c>.</param>
    /// <param name="displayNames">Resolves the name.</param>
    /// <returns>The code with its name.</returns>
    public static OwnerLanguageDto ToDto(string language, IStringLocalizer<DisplayNames> displayNames)
    {
        return new OwnerLanguageDto(language, NameOf(language, displayNames));
    }
}
