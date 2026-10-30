using System.Diagnostics.CodeAnalysis;
using Maran.SharedKernel.Enums;

namespace Maran.SharedKernel.Pagination;

/// <summary>
/// One parsed sort request: the public name of a field and the direction to order it in. Written
/// on the wire as <c>name</c> (ascending) or <c>-name</c> (descending).
/// </summary>
/// <param name="Field">The public field name, as a <see cref="SortMap{T}"/> key.</param>
/// <param name="Direction">The direction to order by <paramref name="Field"/>.</param>
public sealed record SortKey(string Field, SortDirection Direction)
{
    /// <summary>The prefix that marks a descending key.</summary>
    private const char DescendingPrefix = '-';

    /// <summary>
    /// Parses a wire sort key. A field name is an ASCII letter followed by ASCII letters and digits;
    /// anything else — the empty string, a lone minus, a space, a dot, a second minus — is refused.
    /// </summary>
    /// <param name="text">The wire text, such as <c>name</c> or <c>-createdAt</c>.</param>
    /// <param name="key">The parsed key when the text is well formed; null otherwise.</param>
    /// <returns>True when <paramref name="text"/> is a well-formed sort key.</returns>
    public static bool TryParse(string text, [NotNullWhen(true)] out SortKey? key)
    {
        key = null;

        var descending = text.Length > 0 && text[0] == DescendingPrefix;
        var field = descending ? text[1..] : text;

        if (!IsFieldName(field))
        {
            return false;
        }

        key = new SortKey(field, descending ? SortDirection.Descending : SortDirection.Ascending);
        return true;
    }

    /// <summary>Writes the key back in its wire form.</summary>
    /// <returns><c>name</c> for an ascending key, <c>-name</c> for a descending one.</returns>
    public override string ToString()
    {
        return Direction == SortDirection.Descending ? DescendingPrefix + Field : Field;
    }

    /// <summary>Whether <paramref name="field"/> is an ASCII letter followed by ASCII letters and digits.</summary>
    /// <param name="field">The candidate field name.</param>
    /// <returns>True when the name is well formed.</returns>
    private static bool IsFieldName(string field)
    {
        return field.Length > 0 && char.IsAsciiLetter(field[0]) && field.All(char.IsAsciiLetterOrDigit);
    }
}
