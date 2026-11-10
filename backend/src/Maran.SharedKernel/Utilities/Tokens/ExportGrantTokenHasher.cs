using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Maran.SharedKernel.Utilities.Tokens;

/// <summary>
/// Mints the bearer token of a single-use export grant (issue #127) and reduces it to the digest the database
/// keeps: 256 bits of randomness, hashed with SHA-256, compared in constant time.
/// </summary>
/// <remarks>
/// <para>
/// Pure and deterministic: the same token always gives the same digest, with no key, salt or parameter, which is
/// what makes it a utility and not a piece of <c>Security/</c> (rules/csharp.md "<c>Security/</c>,
/// <c>Utilities/</c> and a module's <c>Common/</c>"). A token of 256 random bits needs no slow hash; one that a
/// person chose would.
/// </para>
/// <para>
/// The token is shown to the issuing administrator once and never stored. It travels in an
/// <c>Authorization: Bearer</c> header, never in a URL or a query string.
/// </para>
/// </remarks>
public static class ExportGrantTokenHasher
{
    /// <summary>The number of random bytes in a token.</summary>
    public const int TokenBytes = 32;

    /// <summary>The length of a minted token: its bytes in unpadded base64url, which is what a presented value is checked against before anything is looked up.</summary>
    public const int TokenLength = 43;

    /// <summary>Mints a token and its digest.</summary>
    /// <returns>The plaintext token (shown once) and the lowercase hexadecimal SHA-256 to store.</returns>
    public static (string Token, string Digest) Mint()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

        return (token, Digest(token));
    }

    /// <summary>Reduces a presented token to the digest that is stored.</summary>
    /// <param name="token">The token exactly as it was presented.</param>
    /// <returns>The lowercase hexadecimal SHA-256.</returns>
    public static string Digest(string token)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>Compares two digests without revealing, by timing, how many leading characters agree.</summary>
    /// <param name="left">One digest.</param>
    /// <param name="right">The other digest.</param>
    /// <returns><c>true</c> only when they are equal.</returns>
    public static bool DigestsMatch(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    }
}
