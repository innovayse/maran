namespace Maran.SharedKernel.Pagination;

/// <summary>
/// A list request's sort, in exactly one of two states: <see cref="Default"/> — the list's own
/// default order — or a well-formed <see cref="SortKey"/>. Whether that key names a field the list
/// exposes is not this type's question; the list's <see cref="SortMap{T}"/> answers it.
/// </summary>
/// <remarks>
/// <b><c>default</c> is <see cref="Default"/></b>, by construction (the key field's default is null
/// and a key is never null), so a list query declares <c>SortRequest Sort = default</c> and an absent
/// parameter needs no special case. <see cref="Key"/> cannot be read off <see cref="Default"/>.
/// </remarks>
public readonly record struct SortRequest
{
    /// <summary>The requested key, or null for <see cref="Default"/> — private, so null never escapes as a state.</summary>
    private readonly SortKey? _key;

    /// <summary>The list's default order.</summary>
    public static SortRequest Default
    {
        get { return default; }
    }

    /// <summary>Whether this is <see cref="Default"/>.</summary>
    public bool IsDefault
    {
        get { return _key is null; }
    }

    /// <summary>The requested key.</summary>
    /// <exception cref="InvalidOperationException">This is <see cref="Default"/>, which names no key.</exception>
    public SortKey Key
    {
        get
        {
            if (_key is null)
            {
                throw new InvalidOperationException("The default order names no key; branch on IsDefault first.");
            }

            return _key;
        }
    }

    /// <summary>Wraps a parsed key.</summary>
    /// <param name="key">The key.</param>
    private SortRequest(SortKey key)
    {
        _key = key;
    }

    /// <summary>Requests ordering by <paramref name="key"/>.</summary>
    /// <param name="key">The key to order by.</param>
    /// <returns>The request.</returns>
    public static SortRequest Of(SortKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new SortRequest(key);
    }

    /// <summary>
    /// Reads a caller's sort text: null or empty is <see cref="Default"/>; anything else must be a
    /// well-formed <see cref="SortKey"/> (<c>name</c>, <c>-createdAt</c>). Whitespace is not trimmed —
    /// a sort is a field name, not free text, so a space in it is malformed.
    /// </summary>
    /// <param name="text">The text as it arrived, or null when the caller sent none.</param>
    /// <param name="request">The parsed request when well formed; <see cref="Default"/> otherwise.</param>
    /// <returns>True when the text is empty or a well-formed key.</returns>
    public static bool TryParse(string? text, out SortRequest request)
    {
        request = Default;

        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (!SortKey.TryParse(text, out var key))
        {
            return false;
        }

        request = new SortRequest(key);
        return true;
    }

    /// <summary>Builds a request from text the caller knows is well formed, such as a constant in a test.</summary>
    /// <param name="text">The sort text; empty for <see cref="Default"/>.</param>
    /// <returns>The parsed request.</returns>
    /// <exception cref="ArgumentException">The text is not a well-formed sort.</exception>
    public static SortRequest Parse(string text)
    {
        if (!TryParse(text, out var request))
        {
            throw new ArgumentException($"'{text}' is not a well-formed sort.", nameof(text));
        }

        return request;
    }

    /// <summary>The wire form: the key's text, or the empty string for <see cref="Default"/>.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return _key?.ToString() ?? string.Empty;
    }
}
