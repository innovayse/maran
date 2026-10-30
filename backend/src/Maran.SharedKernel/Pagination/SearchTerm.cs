using Maran.SharedKernel.Enums;

namespace Maran.SharedKernel.Pagination;

/// <summary>
/// A list request's search, in exactly one of two states: <see cref="None"/>, or a term carrying a
/// trimmed, non-empty text that holds no control character and is at most
/// <see cref="PageDefaults.SearchMaxLength"/> long. No other value can be constructed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Absent, empty and whitespace are one state.</b> The query string spells "no search" three ways
/// (<c>?</c>, <c>?search=</c>, <c>?search=%20</c>) and the type collapses them at parse time, so no
/// handler can treat them differently and no caller has to omit an empty parameter to be understood.
/// A whitespace-only search used to reach the database as the pattern <c>%  %</c>.
/// </para>
/// <para>
/// <b><c>default</c> is <see cref="None"/></b>, by construction: the text is held in a field whose
/// default is null, and null is never a term's text. That is what lets a list query declare
/// <c>SearchTerm Search = default</c> and what the model binder relies on when the parameter is absent.
/// </para>
/// <para>
/// <b><see cref="Value"/> cannot be read off <see cref="None"/></b> — it throws. A handler branches on
/// <see cref="IsNone"/> first, which is the only reading the type permits.
/// </para>
/// </remarks>
public readonly record struct SearchTerm
{
    /// <summary>The trimmed term, or null for <see cref="None"/> — private, so null never escapes as a state.</summary>
    private readonly string? _text;

    /// <summary>No search: every row matches.</summary>
    public static SearchTerm None
    {
        get { return default; }
    }

    /// <summary>Whether this is <see cref="None"/>.</summary>
    public bool IsNone
    {
        get { return _text is null; }
    }

    /// <summary>The trimmed, non-empty term.</summary>
    /// <exception cref="InvalidOperationException">This is <see cref="None"/>, which has no text.</exception>
    public string Value
    {
        get
        {
            if (_text is null)
            {
                throw new InvalidOperationException("No search has no text; branch on IsNone first.");
            }

            return _text;
        }
    }

    /// <summary>Wraps text already known to be a trimmed, valid, non-empty term.</summary>
    /// <param name="text">The term.</param>
    private SearchTerm(string text)
    {
        _text = text;
    }

    /// <summary>
    /// Reads a caller's search text. Null, empty and whitespace-only text is <see cref="None"/>;
    /// anything else is trimmed and must hold no control character and fit
    /// <see cref="PageDefaults.SearchMaxLength"/>.
    /// </summary>
    /// <remarks>
    /// Control characters are looked for BEFORE trimming: <see cref="string.Trim()"/> removes a
    /// trailing newline, and a term that arrived carrying one is refused rather than repaired.
    /// </remarks>
    /// <param name="text">The text as it arrived, or null when the caller sent none.</param>
    /// <param name="term">The parsed term when accepted; <see cref="None"/> otherwise.</param>
    /// <returns>The verdict: accepted, or the rule the text broke.</returns>
    public static SearchTermVerdict Parse(string? text, out SearchTerm term)
    {
        term = None;

        if (text is null)
        {
            return SearchTermVerdict.Accepted;
        }

        if (text.Any(char.IsControl))
        {
            return SearchTermVerdict.ControlCharacter;
        }

        var trimmed = text.Trim();
        if (trimmed.Length > PageDefaults.SearchMaxLength)
        {
            return SearchTermVerdict.TooLong;
        }

        if (trimmed.Length > 0)
        {
            term = new SearchTerm(trimmed);
        }

        return SearchTermVerdict.Accepted;
    }

    /// <summary>Builds a term from text the caller knows is valid, such as a constant in a test or a job.</summary>
    /// <param name="text">The search text.</param>
    /// <returns>The parsed term; <see cref="None"/> for empty or whitespace text.</returns>
    /// <exception cref="ArgumentException">The text is not a valid search.</exception>
    public static SearchTerm Of(string text)
    {
        var verdict = Parse(text, out var term);
        if (verdict != SearchTermVerdict.Accepted)
        {
            throw new ArgumentException($"The text is not a valid search: {verdict}.", nameof(text));
        }

        return term;
    }

    /// <summary>The term's text, or the empty string for <see cref="None"/> — for logs and test output only.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return _text ?? string.Empty;
    }
}
