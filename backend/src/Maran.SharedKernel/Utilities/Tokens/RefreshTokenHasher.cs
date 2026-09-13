using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Maran.SharedKernel.Utilities.Tokens;

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
///
/// <b>Why <c>Utilities/Tokens/</c> and not <c>Security/</c>, given that
/// <see cref="SharedKernel.Security.Argon2idPasswordHasher"/> also hashes.</b> The line the map
/// draws is not "hashing" — it is whether the output depends on anything but the input.
/// This type is a pure deterministic function: no key, no salt, no cost parameters, no
/// registration, nothing an operator can configure. <c>Security/</c> holds the things that carry a
/// secret or state a policy — the encryption key, the Argon2id cost parameters and the rehash
/// upgrade path they drive, the redaction floor, the sensitive-string types. A digest with no
/// dial on it is a utility, and it sits beside the other pure rules the whole panel may ask
/// (rules/csharp.md, "Security/, Utilities/ and a module's Common/").
/// </remarks>
public static class RefreshTokenHasher
{
    /// <summary>Length of a generated token, in bytes.</summary>
    private const int TokenBytes = 32;

    /// <summary>Generates a new refresh token.</summary>
    /// <returns>A base64url-encoded token, safe to put in a cookie.</returns>
    public static string Generate()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
    }

    /// <summary>Reduces a token to the digest stored in the database.</summary>
    /// <param name="token">The plaintext refresh token.</param>
    /// <returns>The base64-encoded SHA-256 digest.</returns>
    public static string Hash(string token)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
