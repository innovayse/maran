namespace Maran.SharedKernel.Results;

/// <summary>
/// Typed data a failed <see cref="Result{T}"/> publishes into the RFC 7807 problem response's
/// extension members, beside the panel's two standing ones (<c>code</c>, <c>correlationId</c>).
/// This is the panel's ONE convention for carrying failure facts a caller must see — how far a
/// partial operation got, above all — because <see cref="Error"/> deliberately carries a code and
/// nothing else, and a failed result carries no value.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape.</b> One extension per failure: <see cref="Key"/> becomes the member name in the
/// problem JSON, <see cref="Value"/> is a typed record whose properties serialize as that member's
/// camelCase fields. An operation with two facts to publish puts two properties on one record,
/// never two extensions — a second seat would be a second convention. The one place a carried
/// extension is written to the wire is <c>ApiResultExtensions.ToProblemResult</c>
/// (<c>Maran.Sdk</c>), which every failed result travels through; nothing else may write problem
/// extensions. First user: the Backups module's partial-restore counts, under the key
/// <c>restore</c>.
/// </para>
/// <para>
/// <b>The security floor, stated where the type is built.</b> The value reaches the BROWSER
/// verbatim, on the failure path, in front of a customer: it may carry counts, booleans and
/// enum-like facts about the caller's own operation, and it must never carry a path, tool output,
/// a connection string, an address, or anything acting as a secret (rules/security.md item 8).
/// Diagnostic text stays in the server's log, exactly as it does for <see cref="Error"/>.
/// </para>
/// <para>
/// <b>Why the constructor is private.</b> An extension named <c>code</c> would silently overwrite
/// the machine code every screen branches on, and one named <c>detail</c> would collide with the
/// RFC's own member; both would pass review as one more dictionary write. The reserved names are
/// therefore refused at the only construction site, not left to the pipeline's care.
/// </para>
/// <para>
/// <b>Why the seat is on <see cref="Result{T}"/> only.</b> The non-generic <c>Result</c> carries
/// none: no void operation has failure facts to publish today, and a speculative seat is what
/// YAGNI forbids (rules/architecture.md). The day one does, it gains the same seat and this
/// sentence goes.
/// </para>
/// </remarks>
public sealed record ProblemExtension
{
    /// <summary>
    /// Member names an extension may not claim: RFC 7807's own five, plus the two the panel already
    /// writes on every problem response. Ordinal, because keys are constrained to camelCase ASCII
    /// below, so a case-insensitive near-miss cannot be constructed in the first place.
    /// </summary>
    private static readonly string[] ReservedKeys =
    [
        "type", "title", "status", "detail", "instance", "code", "correlationId",
    ];

    /// <summary>The extension member name this payload is published under, in the problem JSON.</summary>
    public string Key { get; }

    /// <summary>
    /// The typed payload serialized as the member's value. A record with camelCase-serializing
    /// properties; see the class remarks for what it may and may not carry.
    /// </summary>
    public object Value { get; }

    /// <summary>Creates the pair; reachable only through <see cref="Of"/>, which validates the key.</summary>
    /// <param name="key">The already-validated extension member name.</param>
    /// <param name="value">The typed payload.</param>
    private ProblemExtension(string key, object value)
    {
        Key = key;
        Value = value;
    }

    /// <summary>Creates an extension, refusing a key that could corrupt the problem response.</summary>
    /// <param name="key">
    /// The extension member name: a camelCase ASCII identifier (a lower-case letter, then letters
    /// and digits), and none of the reserved member names.
    /// </param>
    /// <param name="value">The typed payload published under that name.</param>
    /// <returns>The validated extension.</returns>
    /// <exception cref="ArgumentException">
    /// The key is empty, not a camelCase ASCII identifier, or a reserved member name.
    /// </exception>
    public static ProblemExtension Of(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!IsCamelCaseIdentifier(key))
        {
            throw new ArgumentException(
                $"Problem extension key '{key}' must be a camelCase ASCII identifier.", nameof(key));
        }

        if (Array.IndexOf(ReservedKeys, key) >= 0)
        {
            throw new ArgumentException(
                $"Problem extension key '{key}' is a reserved problem member name.", nameof(key));
        }

        return new ProblemExtension(key, value);
    }

    /// <summary>Whether <paramref name="key"/> is a lower-case ASCII letter followed by ASCII letters and digits.</summary>
    /// <param name="key">The candidate member name.</param>
    /// <returns><c>true</c> when the key has the shape every panel extension member uses.</returns>
    private static bool IsCamelCaseIdentifier(string key)
    {
        if (key.Length == 0 || !char.IsAsciiLetterLower(key[0]))
        {
            return false;
        }

        foreach (var character in key)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
