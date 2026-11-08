using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// What the module accepts as an IP address: strictly one spelling per address, so the same address is never
/// two values and a parser's leniency never reaches a zone file. Used by record values and by the settings.
/// </summary>
public static class IpAddressRule
{
    /// <summary>Tells whether text is a dotted-quad IPv4 address with no leading zeros.</summary>
    /// <param name="value">The text.</param>
    /// <returns>
    /// True for exactly four decimal parts of 0 to 255. The shorthand forms <c>127.1</c> and <c>0x7f.1</c> and
    /// the leading-zero form <c>010.1.1.1</c>, which some parsers read as octal, are refused.
    /// </returns>
    public static bool IsIpv4(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        return parts.All(part =>
        {
            return part.Length is >= 1 and <= 3
                && part.All(char.IsAsciiDigit)
                && (part.Length == 1 || part[0] != '0')
                && int.Parse(part, CultureInfo.InvariantCulture) <= 255;
        });
    }

    /// <summary>Tells whether text is an IPv6 address with no scope id.</summary>
    /// <param name="value">The text.</param>
    /// <returns>True when it parses as IPv6 using only hexadecimal digits, colons and dots.</returns>
    public static bool IsIpv6(string value)
    {
        var onlyAddressCharacters = value.All(character =>
        {
            return char.IsAsciiHexDigit(character) || character is ':' or '.';
        });
        if (!value.Contains(':') || !onlyAddressCharacters)
        {
            return false;
        }

        return IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6;
    }

    /// <summary>Gives an IPv6 address its canonical lower-case spelling, or returns the text unchanged.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The canonical spelling when <paramref name="value"/> is an IPv6 address.</returns>
    public static string CanonicalIpv6(string value)
    {
        return IsIpv6(value) ? IPAddress.Parse(value).ToString() : value;
    }
}
