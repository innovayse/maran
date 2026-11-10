namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>What a zone template's name and description may be, and the one spelling a name is compared in.</summary>
public static class TemplateNamePolicy
{
    /// <summary>The longest template name, in characters.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The longest template description, in characters.</summary>
    public const int MaxDescriptionLength = 256;

    /// <summary>Spells a name the way it is stored: trimmed.</summary>
    /// <param name="name">The name as typed.</param>
    /// <returns>The stored spelling. Normalising decides nothing; <see cref="IsNameValid"/> does.</returns>
    public static string Normalize(string? name)
    {
        return (name ?? string.Empty).Trim();
    }

    /// <summary>Spells a name the way two names are compared for being the same: trimmed and lower case.</summary>
    /// <param name="name">The name as typed.</param>
    /// <returns>The comparison spelling, which a unique index holds.</returns>
    public static string Key(string? name)
    {
        return Normalize(name).ToLowerInvariant();
    }

    /// <summary>Tells whether a normalised name is acceptable.</summary>
    /// <param name="normalized">A name already passed through <see cref="Normalize"/>.</param>
    /// <returns>True for 1 to <see cref="MaxNameLength"/> characters with no control character.</returns>
    public static bool IsNameValid(string normalized)
    {
        return normalized.Length is > 0 and <= MaxNameLength && !normalized.Any(char.IsControl);
    }

    /// <summary>Tells whether a description is acceptable.</summary>
    /// <param name="description">The description as typed; empty is acceptable.</param>
    /// <returns>True for at most <see cref="MaxDescriptionLength"/> characters with no control character.</returns>
    public static bool IsDescriptionValid(string? description)
    {
        var text = description ?? string.Empty;

        return text.Length <= MaxDescriptionLength && !text.Any(char.IsControl);
    }
}
