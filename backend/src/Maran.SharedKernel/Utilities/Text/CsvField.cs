namespace Maran.SharedKernel.Utilities.Text;

/// <summary>
/// The rule for writing one value as one field of an RFC 4180 CSV record, safe to open in a
/// spreadsheet.
/// </summary>
/// <remarks>
/// <para>
/// <b>Quoting.</b> A field containing a comma, a double quote, a carriage return or a line feed is
/// wrapped in double quotes, with each embedded double quote doubled. Any other field is written as it
/// is. The caller joins fields with commas and ends a record with CRLF.
/// </para>
/// <para>
/// <b>Formula neutralising.</b> A spreadsheet treats a cell that begins with <c>=</c>, <c>+</c>,
/// <c>-</c>, <c>@</c>, a tab or a carriage return as a formula and evaluates it, so a value an attacker
/// typed (a claimed user name, a user-agent header) becomes code on the machine of whoever opens the
/// file. Such a value gets a leading apostrophe, which the spreadsheet displays as nothing and treats as
/// text. The test is made on the raw value, before quoting. The apostrophe is data to any program that
/// is not a spreadsheet, so a lossless export must be offered in another format.
/// </para>
/// <para>
/// It lives in SharedKernel because every module that offers a CSV file has the same exposure, and a
/// module may not import another's types.
/// </para>
/// </remarks>
public static class CsvField
{
    /// <summary>The first characters that make a spreadsheet read a cell as a formula.</summary>
    private const string FormulaLeaders = "=+-@\t\r";

    /// <summary>The characters that force a field to be quoted.</summary>
    private static readonly char[] QuotingTriggers = [',', '"', '\r', '\n'];

    /// <summary>Writes a value as one CSV field.</summary>
    /// <param name="value">The value, or <c>null</c> for an empty field.</param>
    /// <returns>The field text, quoted when it needs quoting and neutralised when it begins like a formula.</returns>
    public static string Of(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = FormulaLeaders.Contains(value[0]) ? "'" + value : value;

        if (text.IndexOfAny(QuotingTriggers) < 0)
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
