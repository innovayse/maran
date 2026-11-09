namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// The alphabet of record text (TXT bodies and CAA values): printable ASCII, 0x20 to 0x7E, without a double
/// quote or a backslash. The value is rendered between quotes in a line-oriented file, so those two are the
/// characters that could end the quote or begin an escape; they are refused rather than escaped, because a
/// DKIM, SPF or DMARC value never needs them (rules/security.md item 4).
/// </summary>
public static class RecordTextRule
{
    /// <summary>Tells whether a value is non-empty, within the length bound, and made only of allowed characters.</summary>
    /// <param name="value">The text.</param>
    /// <returns>True when it may be stored and rendered.</returns>
    public static bool IsAcceptable(string value)
    {
        if (value.Length is 0 or > DnsLimits.MaxValueLength)
        {
            return false;
        }

        return value.All(character =>
        {
            return character is >= ' ' and <= '~' && character != '"' && character != '\\';
        });
    }
}
