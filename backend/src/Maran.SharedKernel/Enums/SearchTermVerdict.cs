namespace Maran.SharedKernel.Enums;

/// <summary>
/// What <see cref="Pagination.SearchTerm.Parse"/> decided about a caller's search text: accepted, or
/// the one rule it broke. Each refusal maps to its own error code at the HTTP boundary, so a caller
/// can tell "too long" from "not allowed" without parsing a sentence.
/// </summary>
public enum SearchTermVerdict
{
    /// <summary>The text is a valid search, or no search at all (absent, empty or whitespace).</summary>
    Accepted = 0,

    /// <summary>The text carries a control character — a newline, a tab, a NUL.</summary>
    ControlCharacter = 1,

    /// <summary>The trimmed text is longer than <see cref="Pagination.PageDefaults.SearchMaxLength"/>.</summary>
    TooLong = 2,
}
