using System.Globalization;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Domain.Policies;

/// <summary>
/// The rule about which accent colour a reseller may give its customers' shell: a <c>#rrggbb</c> value
/// that white text can be read on.
/// </summary>
/// <remarks>
/// The shell paints its primary buttons and selected navigation item with the accent as a FILL under
/// white text, so the colour is held to the contrast WCAG 2.2 asks of body-size text (4.5:1) against
/// white, computed with the WCAG relative-luminance formula. A reseller that picked a pale colour would
/// otherwise make every primary button of its customers unreadable, and the panel cannot see it. The
/// rule lives here, on the server, because the SPA's check is advice (rules/security.md item 6).
/// </remarks>
public static class BrandingAccentPolicy
{
    /// <summary>The smallest contrast ratio against white a colour may have: WCAG 2.2 AA for body-size text.</summary>
    public const double MinimumContrastWithWhite = 4.5;

    /// <summary>The length of a <c>#rrggbb</c> value.</summary>
    public const int ColorLength = 7;

    /// <summary>Decides whether a submitted colour may be stored.</summary>
    /// <param name="color">The submitted value.</param>
    /// <returns>Whether it is acceptable, malformed, or too light.</returns>
    public static BrandingAccentVerdict Check(string color)
    {
        ArgumentNullException.ThrowIfNull(color);

        if (color.Length != ColorLength || color[0] != '#')
        {
            return BrandingAccentVerdict.NotAColor;
        }

        var channels = new double[3];
        for (var index = 0; index < channels.Length; index++)
        {
            var pair = color.AsSpan(1 + (index * 2), 2);
            if (!byte.TryParse(pair, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
                || !IsHexDigit(pair[0])
                || !IsHexDigit(pair[1]))
            {
                return BrandingAccentVerdict.NotAColor;
            }

            channels[index] = Linearize(value / 255.0);
        }

        var luminance = (0.2126 * channels[0]) + (0.7152 * channels[1]) + (0.0722 * channels[2]);

        // White has relative luminance 1.0.
        var contrast = 1.05 / (luminance + 0.05);
        return contrast >= MinimumContrastWithWhite
            ? BrandingAccentVerdict.Acceptable
            : BrandingAccentVerdict.LowContrast;
    }

    /// <summary>Converts one sRGB channel to linear light, as the WCAG relative-luminance formula does.</summary>
    /// <param name="channel">The channel as a fraction of full scale.</param>
    /// <returns>The linearised channel.</returns>
    private static double Linearize(double channel)
    {
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    /// <summary>Whether a character is one of the sixteen hexadecimal digits, which <c>byte.TryParse</c> alone would not guarantee (it accepts a sign and spaces in some styles).</summary>
    /// <param name="character">The character to test.</param>
    /// <returns><c>true</c> for 0-9, a-f and A-F.</returns>
    private static bool IsHexDigit(char character)
    {
        return Uri.IsHexDigit(character);
    }
}
