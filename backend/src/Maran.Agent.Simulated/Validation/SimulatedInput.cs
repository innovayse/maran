namespace Maran.Agent.Simulated.Validation;

/// <summary>
/// The minimal alphabet and length checks the simulated agent applies before it remembers a value.
/// </summary>
/// <remarks>
/// The panel's validators have already judged every value. These checks are the agent's own second
/// look (rules/architecture.md: every input is re-validated where it is used), kept deliberately
/// small: they exist so that nothing outside a plain name alphabet is ever stored, shown to the next
/// visitor or used as a key, and so a stored string has a bounded length. They do not restate the
/// panel's product rules.
/// </remarks>
public static class SimulatedInput
{
    /// <summary>Whether <paramref name="value"/> is present and short enough to bound memory.</summary>
    /// <param name="value">The account, suffix, id or schedule field to check.</param>
    /// <param name="maxLength">The longest accepted text.</param>
    /// <returns>True when it is not blank and no longer than <paramref name="maxLength"/>.</returns>
    public static bool IsBoundedName(string? value, int maxLength)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;
    }

    /// <summary>Whether <paramref name="value"/> is a plausible system user name.</summary>
    /// <param name="value">The text to judge.</param>
    /// <param name="maxLength">The longest accepted name.</param>
    /// <returns>
    /// True for a non-empty name that starts with a lower-case letter or an underscore and continues
    /// with lower-case letters, digits, underscores or hyphens.
    /// </returns>
    public static bool IsAccountName(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(value[0]) && value[0] != '_')
        {
            return false;
        }

        foreach (var character in value)
        {
            var allowed = char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character == '_'
                || character == '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="value"/> is a plausible lower-case DNS host name.</summary>
    /// <param name="value">The text to judge.</param>
    /// <param name="maxLength">The longest accepted name.</param>
    /// <returns>
    /// True for a non-empty name of lower-case letters, digits, hyphens and dots that neither starts
    /// nor ends with a dot and has no empty label.
    /// </returns>
    public static bool IsDomain(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength)
        {
            return false;
        }

        if (value[0] == '.' || value[^1] == '.' || value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in value)
        {
            var allowed = char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character == '-'
                || character == '.';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="value"/> is a two-component PHP version such as <c>8.3</c>.</summary>
    /// <param name="value">The text to judge.</param>
    /// <returns>True for one or two digits, a dot, then one or two digits.</returns>
    public static bool IsPhpVersion(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = value.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        return IsShortNumber(parts[0]) && IsShortNumber(parts[1]);
    }

    /// <summary>Whether <paramref name="value"/> is a plausible ACME token: URL-safe base64 characters.</summary>
    /// <param name="value">The text to judge.</param>
    /// <param name="maxLength">The longest accepted token.</param>
    /// <returns>True for a non-empty run of letters, digits, hyphens and underscores.</returns>
    public static bool IsToken(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="value"/> is bounded free text without control characters.</summary>
    /// <param name="value">The text to judge; the empty string is accepted.</param>
    /// <param name="maxLength">The longest accepted text.</param>
    /// <returns>True when the text is no longer than the cap and holds no control character.</returns>
    public static bool IsPlainText(string? value, int maxLength)
    {
        if (value is null || value.Length > maxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="value"/> is one or two ASCII digits.</summary>
    /// <param name="value">The text to judge.</param>
    /// <returns>True for a one- or two-digit number.</returns>
    private static bool IsShortNumber(string value)
    {
        return value.Length is >= 1 and <= 2 && value.All(char.IsAsciiDigit);
    }
}
