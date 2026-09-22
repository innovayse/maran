using System.Security.Cryptography;
using System.Text;

namespace Maran.Modules.Identity.Common;

/// <summary>
/// Generates refresh tokens and reduces them to the digest stored beside a session.
/// </summary>
/// <remarks>
/// SHA-256 is correct here where Argon2id would be wrong. A refresh token is 256 bits straight from
/// the system CSPRNG, not something a human chose: there is no guessable structure to slow an
/// attacker down against, so a deliberately expensive hash would buy nothing and cost a hundred
/// milliseconds on every refresh — a call the SPA makes on every page load. rules/security.md
/// item 9 bans MD5 and SHA-1 for anything security-relevant; SHA-256 over a full-entropy secret is
/// exactly what it is for.
/// </remarks>
public static class RefreshTokenHasher
{
    /// <summary>Length of a generated token, in bytes.</summary>
    private const int TokenBytes = 32;

    /// <summary>Generates a new refresh token.</summary>
    /// <returns>A base64url-encoded token, safe to put in a cookie.</returns>
    public static string Generate()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
    }

    /// <summary>Reduces a token to the digest stored in the database.</summary>
    /// <param name="token">The plaintext refresh token.</param>
    /// <returns>The base64-encoded SHA-256 digest.</returns>
    public static string Hash(string token)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>Encodes bytes in the URL- and cookie-safe base64 alphabet, without padding.</summary>
    /// <param name="value">The bytes to encode.</param>
    /// <returns>The encoded text.</returns>
    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
