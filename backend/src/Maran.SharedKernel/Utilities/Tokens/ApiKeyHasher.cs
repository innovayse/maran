using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Maran.SharedKernel.Utilities.Tokens;

/// <summary>
/// Mints API keys, recognises their shape, and reduces them to the digest stored beside a key row.
/// </summary>
/// <remarks>
/// <para>
/// A key reads <c>mk_&lt;8 lowercase hex&gt;_&lt;43 base64url characters&gt;</c>: 55 characters. The
/// 43 characters are 32 bytes straight from the system CSPRNG; the 8 hex characters are 4 further
/// bytes drawn separately, never cut from the secret, so showing the prefix on a list screen
/// discloses none of the 256 secret bits. The prefix is an index into the key table and nothing
/// more; it authenticates nobody.
/// </para>
/// <para>
/// SHA-256 over the WHOLE presented string (prefix included) is correct here for the reason
/// <see cref="RefreshTokenHasher"/> states: a slow hash protects a low-entropy secret from offline
/// search, and a 256-bit secret has none to protect against. This type is a pure deterministic
/// function with no key, salt or dial, so it is a utility and not <c>Security/</c>. The digest is
/// base64 text, the same encoding <see cref="RefreshTokenHasher.Hash"/> produces.
/// </para>
/// <para>
/// <see cref="TryParse"/> only judges SHAPE: it refuses a value that cannot be a key before any
/// hashing or lookup happens, and it never throws, whatever it is handed. It says nothing about
/// whether the key exists. The last base64url character is not checked for canonical trailing
/// bits; a non-canonical spelling simply hashes to a digest no row holds.
/// </para>
/// </remarks>
public static class ApiKeyHasher
{
    /// <summary>The fixed start of every key.</summary>
    private const string KeyMarker = "mk_";

    /// <summary>Number of random bytes behind the prefix.</summary>
    private const int PrefixBytes = 4;

    /// <summary>Number of random bytes behind the secret part.</summary>
    private const int SecretBytes = 32;

    /// <summary>Length of the prefix, in hex characters.</summary>
    private const int PrefixLength = 8;

    /// <summary>Length of the secret part, in base64url characters (32 bytes, unpadded).</summary>
    private const int SecretLength = 43;

    /// <summary>The separator between prefix and secret.</summary>
    private const char Separator = '_';

    /// <summary>The exact length of every key: marker, prefix, separator, secret.</summary>
    public const int KeyLength = 3 + PrefixLength + 1 + SecretLength;

    /// <summary>Generates a new key.</summary>
    /// <returns>
    /// The plaintext key (shown once, never stored), its public prefix, and the digest to store.
    /// </returns>
    public static (string Plaintext, string Prefix, string Digest) Mint()
    {
        var prefix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(PrefixBytes));
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));
        var plaintext = KeyMarker + prefix + Separator + secret;

        return (plaintext, prefix, Digest(plaintext));
    }

    /// <summary>Reduces a key to the digest stored in the database.</summary>
    /// <param name="plaintext">The whole key, exactly as it was presented.</param>
    /// <returns>The base64-encoded SHA-256 digest.</returns>
    public static string Digest(string plaintext)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
    }

    /// <summary>
    /// Says whether a header value has the shape of a key and extracts its prefix. Never throws.
    /// </summary>
    /// <param name="header">The raw header value; null, empty or any other text is accepted.</param>
    /// <param name="prefix">The 8-character prefix when the shape is valid; otherwise empty.</param>
    /// <returns>True only for exactly <see cref="KeyLength"/> characters of the key shape.</returns>
    public static bool TryParse(string? header, out string prefix)
    {
        prefix = string.Empty;

        if (header is null || header.Length != KeyLength || !header.StartsWith(KeyMarker, StringComparison.Ordinal))
        {
            return false;
        }

        var separatorIndex = KeyMarker.Length + PrefixLength;

        if (header[separatorIndex] != Separator)
        {
            return false;
        }

        for (var i = KeyMarker.Length; i < separatorIndex; i++)
        {
            if (!IsLowerHex(header[i]))
            {
                return false;
            }
        }

        for (var i = separatorIndex + 1; i < header.Length; i++)
        {
            if (!IsBase64UrlCharacter(header[i]))
            {
                return false;
            }
        }

        prefix = header.Substring(KeyMarker.Length, PrefixLength);
        return true;
    }

    /// <summary>Compares two stored-format digests in constant time.</summary>
    /// <param name="left">One digest, as <see cref="Digest"/> returns it.</param>
    /// <param name="right">The other digest.</param>
    /// <returns>True only when both are the same text; a different length is false at once.</returns>
    public static bool DigestsMatch(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }

    /// <summary>Whether a character is one of <c>0-9a-f</c> (ASCII only, lower case only).</summary>
    /// <param name="c">The character.</param>
    /// <returns>True for a lower-case hex digit.</returns>
    private static bool IsLowerHex(char c)
    {
        return c is >= '0' and <= '9' or >= 'a' and <= 'f';
    }

    /// <summary>Whether a character is in the unpadded base64url alphabet (ASCII only).</summary>
    /// <param name="c">The character.</param>
    /// <returns>True for <c>A-Z a-z 0-9 - _</c>.</returns>
    private static bool IsBase64UrlCharacter(char c)
    {
        return c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';
    }
}
