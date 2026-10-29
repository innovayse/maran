using System.Security.Cryptography;
using System.Text;

namespace Maran.Host.Tests.Fixtures;

/// <summary>
/// Deterministic, random-looking key material for tests. Each key is a hash of a label, so no
/// generator is drawn from (a test must repeat exactly) and no new key literal is committed (a
/// committed key is a published key). The bytes of a hash are uniformly spread, which is what the
/// key-quality policy must accept.
/// </summary>
public static class DerivedTestKeys
{
    /// <summary>The most bytes a derived key can hold, the size of a SHA-512 digest.</summary>
    private const int MaxLength = 64;

    /// <summary>The largest size a SHA-256 digest covers; beyond it the SHA-512 digest is used.</summary>
    private const int Sha256Length = 32;

    /// <summary>Derives raw key bytes from a label.</summary>
    /// <param name="label">Names the key; the same label always yields the same bytes.</param>
    /// <param name="length">How many bytes to return, from 1 to 64.</param>
    /// <returns>The first <paramref name="length"/> bytes of the label's digest.</returns>
    public static byte[] Bytes(string label, int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxLength);

        var labelBytes = Encoding.UTF8.GetBytes(label);
        var digest = length <= Sha256Length ? SHA256.HashData(labelBytes) : SHA512.HashData(labelBytes);
        return digest[..length];
    }

    /// <summary>Derives a key from a label and returns it base64-encoded, as it is configured.</summary>
    /// <param name="label">Names the key; the same label always yields the same key.</param>
    /// <param name="length">How many bytes the key holds, from 1 to 64.</param>
    /// <returns>The base64 text of the derived bytes.</returns>
    public static string Base64(string label, int length)
    {
        return Convert.ToBase64String(Bytes(label, length));
    }
}
