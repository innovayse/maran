namespace Maran.SharedKernel.Pagination;

/// <summary>
/// A list request's optional text filter — one machine-stable value the rows must equal exactly
/// (a task kind, an audit action, a PHP version) — in exactly one of two states: <see cref="None"/>,
/// or a non-empty value. No other value can be constructed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a type and not a string.</b> A <c>string</c> filter needs a sentinel for "no filter": the
/// empty string, with null refused so that "every" has one spelling. That is one field carrying two
/// meanings, and every handler and validator had to remember which spelling was the state. Here the
/// state is the type's: a handler branches on <see cref="IsNone"/>, and <see cref="Value"/> cannot be
/// read off <see cref="None"/>.
/// </para>
/// <para>
/// <b>Absent and empty are one state</b> (<c>?</c>, <c>?kind=</c>). Anything else is kept verbatim —
/// not trimmed — because the filter is an exact match and the module's validator, not this type,
/// decides what text its column can hold: <c>?kind=%20</c> is a value, and the validator refuses it.
/// </para>
/// <para>
/// <b><c>default</c> is <see cref="None"/></b>, by construction, which is what lets a list query
/// declare <c>FilterValue Kind = default</c> and what the model binder relies on when the parameter is
/// absent. The shape mirrors <see cref="SearchTerm"/>, which models a search the same way.
/// </para>
/// </remarks>
public readonly record struct FilterValue
{
    /// <summary>The value, or null for <see cref="None"/> — private, so null never escapes as a state.</summary>
    private readonly string? _value;

    /// <summary>No filter: every row matches.</summary>
    public static FilterValue None
    {
        get { return default; }
    }

    /// <summary>Whether this is <see cref="None"/>.</summary>
    public bool IsNone
    {
        get { return _value is null; }
    }

    /// <summary>The non-empty value the rows must equal.</summary>
    /// <exception cref="InvalidOperationException">This is <see cref="None"/>, which has no value.</exception>
    public string Value
    {
        get
        {
            if (_value is null)
            {
                throw new InvalidOperationException("No filter has no value; branch on IsNone first.");
            }

            return _value;
        }
    }

    /// <summary>Wraps a value already known to be non-empty.</summary>
    /// <param name="value">The value.</param>
    private FilterValue(string value)
    {
        _value = value;
    }

    /// <summary>Reads a caller's filter text: null and empty are <see cref="None"/>, anything else is the value.</summary>
    /// <param name="text">The text as it arrived, or null when the caller sent none.</param>
    /// <returns>The filter.</returns>
    public static FilterValue From(string? text)
    {
        return string.IsNullOrEmpty(text) ? None : new FilterValue(text);
    }

    /// <summary>Builds a filter on a value the caller knows is non-empty, such as a constant in a test.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentException">The value is empty, which is <see cref="None"/> and not a value.</exception>
    public static FilterValue Of(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return new FilterValue(value);
    }

    /// <summary>The value, or the empty string for <see cref="None"/> — for logs and test output only.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return _value ?? string.Empty;
    }
}
