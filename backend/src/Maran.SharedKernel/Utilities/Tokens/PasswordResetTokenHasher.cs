using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Maran.SharedKernel.Utilities.Tokens;

/// <summary>
/// Generates password-reset tokens and reduces them to the digest stored beside a reset request.
/// </summary>
/// <remarks>
/// <para>
/// <b>SHA-256 is correct here where Argon2id would be wrong.</b> A reset token is 32 bytes straight
/// from the system CSPRNG, not something a human chose: there is no guessable structure to slow an
/// attacker down against, so a deliberately expensive hash would buy nothing and would put 100 ms of
/// key derivation on a public, anonymous endpoint. rules/security.md item 9 bans MD5 and SHA-1 for
/// anything security-relevant; SHA-256 over a full-entropy secret is exactly what it is for.
/// </para>
/// <para>
/// <b>Only the digest is stored, and that is the point of the type.</b> The plaintext exists in one
/// request and in one e-mail; a database dump, a replica, or a backup taken during the token's hour
/// yields digests, and a digest is not permission to become the account.
/// </para>
/// <para>
/// <b>It is a separate type from <see cref="RefreshTokenHasher"/> rather than a shared one.</b> The
/// two hash different secrets into different tables with different lifetimes, and a single helper
/// would mean a change made for one — a wider digest, a different encoding — silently rewriting how
/// the other's stored column is interpreted, invalidating every live session or every outstanding
/// reset. The shared thing here is an algorithm the framework already provides, not a policy.
/// </para>
/// <para>
/// <b>Why <c>Utilities/Tokens/</c> and not <c>Security/</c>.</b> Both this type and
/// <see cref="SharedKernel.Security.Argon2idPasswordHasher"/> hash, so "it hashes" cannot be what
/// chooses the folder. What chooses it is whether the output depends on anything but the input.
/// This is a keyless, saltless, unparameterised digest — nothing here is configurable and nothing
/// here holds a secret at rest. <c>Security/</c> is for exactly those two things: the encryption
/// key and the converter that uses it, the Argon2id cost parameters and the rehash path they
/// drive, the redaction floor, the sensitive-string types. See rules/csharp.md,
/// "Security/, Utilities/ and a module's Common/".
/// </para>
/// <para>
/// <b>Leaving Identity did not change a byte.</b> Only the namespace moved; the token length, the
/// encoding, the digest and its base64 rendering are untouched, so every stored digest and every
/// outstanding reset link keeps its meaning.
/// </para>
/// </remarks>
public static class PasswordResetTokenHasher
{
    /// <summary>Length of a generated token, in bytes. Thirty-two, as the plan specifies.</summary>
    private const int TokenBytes = 32;

    /// <summary>Generates a new reset token.</summary>
    /// <returns>A base64url-encoded token, safe to put in a link.</returns>
    public static string Generate()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
    }

    /// <summary>Reduces a token to the digest stored in the database.</summary>
    /// <param name="token">The plaintext reset token as the caller presented it.</param>
    /// <returns>The base64-encoded SHA-256 digest.</returns>
    public static string Hash(string token)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
