using System.Security.Cryptography;
using System.Text;
using Maran.SharedKernel.Interfaces;

namespace Maran.SharedKernel.Security;

/// <summary>
/// HKDF-SHA256 implementation of <see cref="ISubKeyDeriver"/> over the same 256-bit secret
/// <see cref="AesGcmEncryptionService"/> is built from (rules/security.md "Crypto": the BCL's
/// <see cref="HKDF"/> only, nothing home-grown). The purpose is the HKDF info label, which is what
/// separates one derived key from every other use of the secret.
/// </summary>
public sealed class HkdfSubKeyDeriver : ISubKeyDeriver
{
    /// <summary>Required secret size, in bytes; the same 256 bits the cipher requires.</summary>
    private const int SecretSizeBytes = 32;

    /// <summary>Size of every derived key, in bytes.</summary>
    private const int DerivedKeySizeBytes = 32;

    /// <summary>The decoded panel secret the keys are derived from.</summary>
    private readonly byte[] _secret;

    /// <summary>Creates the deriver from the panel's base64-encoded 256-bit secret.</summary>
    /// <param name="base64Secret">The secret, base64-encoded; must decode to exactly 32 bytes.</param>
    /// <exception cref="ArgumentException">The secret is missing, not valid base64, or not 32 bytes once decoded.</exception>
    public HkdfSubKeyDeriver(string base64Secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Secret);

        byte[] secret;
        try
        {
            secret = Convert.FromBase64String(base64Secret);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("The secret must be valid base64.", nameof(base64Secret), ex);
        }

        if (secret.Length != SecretSizeBytes)
        {
            throw new ArgumentException(
                $"The secret must decode to {SecretSizeBytes} bytes; got {secret.Length}.",
                nameof(base64Secret));
        }

        _secret = secret;
    }

    /// <inheritdoc/>
    public byte[] DeriveKey(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256, _secret, DerivedKeySizeBytes, salt: [], info: Encoding.UTF8.GetBytes(purpose));
    }
}
