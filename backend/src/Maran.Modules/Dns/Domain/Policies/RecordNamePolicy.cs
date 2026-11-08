namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>What the owner name of a record may be, relative to its zone.</summary>
public static class RecordNamePolicy
{
    /// <summary>The owner name that means the zone apex itself.</summary>
    public const string Apex = "@";

    /// <summary>The label that matches any single label when it leads a name.</summary>
    public const string Wildcard = "*";

    /// <summary>Spells a name the way it is stored: trimmed and lower case.</summary>
    /// <param name="name">The name as typed.</param>
    /// <returns>The stored spelling. Normalising decides nothing; <see cref="IsValid"/> does.</returns>
    public static string Normalize(string? name)
    {
        return (name ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>Tells whether a normalised owner name is acceptable inside a zone.</summary>
    /// <param name="normalized">A name already passed through <see cref="Normalize"/>.</param>
    /// <param name="zoneName">The zone's apex, which the whole name must still fit under.</param>
    /// <returns>
    /// True for <c>@</c>, or for dot-separated labels of letters, digits, hyphen and underscore (underscore is
    /// how service labels such as <c>_dmarc</c> are spelled), each 1 to 63 characters and neither starting nor
    /// ending with a hyphen, where only the leftmost label may be <c>*</c>. An absolute name (trailing dot),
    /// an empty label and any other character are refused: the name is validated, never escaped.
    /// </returns>
    public static bool IsValid(string normalized, string zoneName)
    {
        if (normalized == Apex)
        {
            return true;
        }

        if (normalized.Length == 0 || normalized.Length + 1 + zoneName.Length > DnsLimits.MaxNameLength)
        {
            return false;
        }

        var labels = normalized.Split('.');
        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index];
            if (label == Wildcard && index == 0)
            {
                continue;
            }

            if (!IsValidLabel(label))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Tells whether one label is a valid, non-wildcard label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>True when it is 1 to 63 allowed characters with no hyphen at either end.</returns>
    private static bool IsValidLabel(string label)
    {
        if (label.Length is 0 or > DnsLimits.MaxLabelLength)
        {
            return false;
        }

        if (label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        return label.All(character =>
        {
            return char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character == '-'
                || character == '_';
        });
    }
}
