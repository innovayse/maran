using System.Text;

namespace Maran.Modules.Updates.Domain.Policies;

/// <summary>
/// Reads the text of a small <c>KEY=value</c> file that root wrote, as untrusted input: it is the one
/// door every such file goes through, so a hostile or garbled file is "nothing" in one place and not a
/// different thing in each reader (rules/security.md item 1).
/// </summary>
/// <remarks>
/// A file larger than <see cref="MaxBytes"/> is refused whole rather than truncated, because a
/// truncated file can end in the middle of a value and read as a different value. Only lines of the form
/// <c>UPPER_CASE_KEY=value</c> are kept; every other line is ignored, and the first assignment of a key
/// wins. The values are returned unchecked — each consumer applies the rule for its own field through
/// <see cref="PanelUpdateValueRules"/>.
/// </remarks>
public static class StateFileText
{
    /// <summary>The largest file, in bytes, that is read at all.</summary>
    public const int MaxBytes = 4096;

    /// <summary>The longest key a line may carry.</summary>
    private const int MaxKeyLength = 32;

    /// <summary>Parses the lines of a state file.</summary>
    /// <param name="text">The file's text, or null when it could not be read.</param>
    /// <returns>The keys and their raw values; empty for a null, oversize or empty file.</returns>
    public static IReadOnlyDictionary<string, string> Parse(string? text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (text is null || Encoding.UTF8.GetByteCount(text) > MaxBytes)
        {
            return values;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator < 1 || separator > MaxKeyLength)
            {
                continue;
            }

            var key = line[..separator];
            if (!IsKey(key))
            {
                continue;
            }

            values.TryAdd(key, line[(separator + 1)..]);
        }

        return values;
    }

    /// <summary>Whether a string is an upper-case key of letters and underscores.</summary>
    /// <param name="key">The text before the first equals sign.</param>
    /// <returns>True for 1 to 32 characters of <c>A-Z</c> and <c>_</c>.</returns>
    private static bool IsKey(string key)
    {
        foreach (var character in key)
        {
            if (character is not ((>= 'A' and <= 'Z') or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
