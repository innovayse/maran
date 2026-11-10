using System.Security.Cryptography;

namespace Maran.SharedKernel.Utilities.Signatures;

/// <summary>
/// The fingerprint an operator compares to know whose key signed a package: the lowercase hexadecimal SHA-256
/// of the key's bytes.
/// </summary>
/// <remarks>
/// One spelling, shared by the panel that shows the fingerprint and the panel that checks the one an operator
/// typed. It is the same value the agent computes over the signer's public key, which is why an import can hand
/// the agent the fingerprint the operator confirmed and have it compared there as well.
/// </remarks>
public static class PublicKeyFingerprint
{
    /// <summary>The length of a fingerprint in characters.</summary>
    public const int Length = 64;

    /// <summary>Computes the fingerprint of a key.</summary>
    /// <param name="publicKey">The key's bytes, exactly as they travel (the uncompressed SEC1 point).</param>
    /// <returns>The 64 lowercase hexadecimal characters of its SHA-256.</returns>
    public static string Of(ReadOnlySpan<byte> publicKey)
    {
        return Convert.ToHexStringLower(SHA256.HashData(publicKey));
    }
}
