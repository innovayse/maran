namespace Maran.Modules.Idempotency.Domain.Policies;

/// <summary>
/// What a caller key, a client key and a request hash must look like before they reach a query or a
/// row. The store checks them even though the action filter has already refused a bad header, because
/// the store is a public seam and a value reaching it unchecked would be written to the database.
/// </summary>
public static class IdempotencyKeyPolicy
{
    /// <summary>The longest a caller key may be, which fits <c>user:</c> or <c>apikey:</c> and a uuid.</summary>
    public const int CallerKeyMaxLength = 128;

    /// <summary>The longest a client-supplied key may be.</summary>
    public const int KeyMaxLength = 255;

    /// <summary>The length of a request fingerprint: HMAC-SHA256 as lowercase hexadecimal.</summary>
    public const int RequestHashLength = 64;

    /// <summary>
    /// True when <paramref name="key"/> is 1 to <see cref="KeyMaxLength"/> characters of
    /// <c>A-Z a-z 0-9 _ - : .</c>. Nothing else — no whitespace, no control character, no non-ASCII
    /// letter — so the value is never a vector for log injection, SQL or a path.
    /// </summary>
    /// <param name="key">The value to test.</param>
    /// <returns>True when the key is acceptable.</returns>
    public static bool IsValidKey(string? key)
    {
        return IsWithinCharset(key, KeyMaxLength);
    }

    /// <summary>
    /// True when <paramref name="callerKey"/> is 1 to <see cref="CallerKeyMaxLength"/> characters of the
    /// same set. A caller key is a typed string such as <c>user:&lt;guid&gt;</c>, which the set covers.
    /// </summary>
    /// <param name="callerKey">The value to test.</param>
    /// <returns>True when the caller key is acceptable.</returns>
    public static bool IsValidCallerKey(string? callerKey)
    {
        return IsWithinCharset(callerKey, CallerKeyMaxLength);
    }

    /// <summary>True when <paramref name="requestHash"/> is exactly 64 lowercase hexadecimal characters.</summary>
    /// <param name="requestHash">The value to test.</param>
    /// <returns>True when the hash is acceptable.</returns>
    public static bool IsValidRequestHash(string? requestHash)
    {
        if (requestHash is null || requestHash.Length != RequestHashLength)
        {
            return false;
        }

        foreach (var character in requestHash)
        {
            var isHex = character is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Tests a value against the key character set and a length ceiling.</summary>
    /// <param name="value">The value to test.</param>
    /// <param name="maxLength">The longest the value may be.</param>
    /// <returns>True when the value is non-empty, within the ceiling and made only of allowed characters.</returns>
    private static bool IsWithinCharset(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            var allowed = character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_' or '-' or ':' or '.';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
