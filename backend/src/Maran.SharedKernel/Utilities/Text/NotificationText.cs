using System.Text;

namespace Maran.SharedKernel.Utilities.Text;

/// <summary>
/// The rule for text that is stored in the notification feed or sent to a webhook: control characters
/// removed, edges trimmed, length capped.
/// </summary>
/// <remarks>
/// Pure and shared because producers in several modules build such text from input they do not
/// control (a task subject, a service manager's words), and a module may not import another's rule.
/// Removal rather than escaping: the feed renders plain text and the unit separator is reserved for
/// joining stored arguments, so a control character has no legitimate use in either.
/// </remarks>
public static class NotificationText
{
    /// <summary>Removes control characters, trims, and cuts to at most a number of code points.</summary>
    /// <param name="value">The text to clean; <c>null</c> reads as empty.</param>
    /// <param name="maxCharacters">The most code points to keep.</param>
    /// <returns>The cleaned text, cut on a code point boundary.</returns>
    public static string Clean(string? value, int maxCharacters)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (!Rune.IsControl(rune))
            {
                builder.Append(rune.ToString());
            }
        }

        return ColumnText.Fit(builder.ToString().Trim(), maxCharacters);
    }
}
