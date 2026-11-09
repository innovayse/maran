using System.Globalization;
using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// The two spellings of a customer PHP setting's value: the one a screen sends and reads (a size is
/// its number of mebibytes, <c>256</c>) and the one php.ini and the agent use (<c>256M</c>).
/// </summary>
/// <remarks>
/// The panel owns the conversion so the SPA never builds or parses a unit suffix, and so the stored
/// value is always the spelling the agent is sent. Every stored size ends in the one suffix this type
/// writes, so reading it back is stripping that suffix and nothing more.
/// </remarks>
public static class PhpSettingSpelling
{
    /// <summary>The suffix php.ini gives a size in mebibytes.</summary>
    private const char MebibyteSuffix = 'M';

    /// <summary>The most characters a number a screen sends may have; keeps the parse inside an int.</summary>
    private const int MaxDigits = 9;

    /// <summary>The most path components a timezone name may have; <c>America/Indiana/Knox</c> is the deepest shape.</summary>
    private const int MaxTimezoneComponents = 3;

    /// <summary>The longest timezone name a screen may send.</summary>
    private const int MaxTimezoneLength = 32;

    /// <summary>Reads the number in a wire value of a numeric kind.</summary>
    /// <param name="wire">The value as a screen sends it: ASCII digits and nothing else.</param>
    /// <param name="number">The number, when the text is one.</param>
    /// <returns>True when <paramref name="wire"/> is a non-negative whole number of at most nine digits.</returns>
    public static bool TryReadNumber(string wire, out int number)
    {
        number = 0;
        if (wire.Length == 0 || wire.Length > MaxDigits || !wire.All(char.IsAsciiDigit))
        {
            return false;
        }

        return int.TryParse(wire, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>Whether <paramref name="value"/> has the shape of an IANA timezone name.</summary>
    /// <param name="value">The name to check.</param>
    /// <returns>
    /// True for one to three components of ASCII letters, digits, <c>_</c>, <c>+</c> and <c>-</c>,
    /// separated by single slashes. By shape and not against a zone database, for the reason the
    /// agent gives: the answer must not depend on what a host's tzdata happens to hold.
    /// </returns>
    public static bool IsTimezoneShape(string value)
    {
        if (value.Length == 0 || value.Length > MaxTimezoneLength)
        {
            return false;
        }

        var components = value.Split('/');
        if (components.Length > MaxTimezoneComponents)
        {
            return false;
        }

        return components.All(component =>
        {
            return component.Length > 0
                && component.All(character =>
                {
                    return char.IsAsciiLetterOrDigit(character) || character is '_' or '+' or '-';
                });
        });
    }

    /// <summary>Restates a screen's value in php.ini's spelling.</summary>
    /// <param name="kind">The setting's kind.</param>
    /// <param name="wire">The value as a screen sends it; the caller has validated it.</param>
    /// <returns>The value the agent is sent and the table stores.</returns>
    public static string ToIni(PhpSettingKind kind, string wire)
    {
        return kind == PhpSettingKind.Size ? wire + MebibyteSuffix : wire;
    }

    /// <summary>Restates a stored value in the spelling a screen reads.</summary>
    /// <param name="kind">The setting's kind.</param>
    /// <param name="ini">The value as the table stores it.</param>
    /// <returns>The value as a screen reads it.</returns>
    public static string ToWire(PhpSettingKind kind, string ini)
    {
        return kind == PhpSettingKind.Size && ini.EndsWith(MebibyteSuffix) ? ini[..^1] : ini;
    }
}
