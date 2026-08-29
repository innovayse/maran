using Maran.SharedKernel.Interfaces;

namespace Maran.SharedKernel.Security;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// AES-256-GCM implementation of <see cref="IEncryptionService"/> (rules/security.md "Crypto": no
/// home-grown crypto — this wraps the BCL's audited <see cref="AesGcm"/> only). The key is supplied
/// by the caller at construction, never hardcoded or read from source; the host resolves it from
/// <c>panel.env</c>-style configuration, never from committed <c>appsettings.json</c>. Construction
/// fails immediately when the key is missing or the wrong size, so a misconfigured server fails at
/// boot rather than at the first encryption call.
/// </summary>
public sealed class AesGcmEncryptionService : IEncryptionService
{
    /// <summary>Required key size for AES-256, in bytes.</summary>
    private const int KeySizeBytes = 32;

    /// <summary>Nonce size recommended for AES-GCM by NIST SP 800-38D.</summary>
    private const int NonceSizeBytes = 12;

    /// <summary>GCM authentication tag size, in bytes.</summary>
    private const int TagSizeBytes = 16;

    /// <summary>The decoded 256-bit key this instance encrypts and decrypts with.</summary>
    private readonly byte[] _key;

    /// <summary>Creates the service from a base64-encoded 256-bit key.</summary>
    /// <param name="base64Key">The encryption key, base64-encoded; must decode to exactly 32 bytes.</param>
    /// <exception cref="ArgumentException">The key is missing, not valid base64, or not 32 bytes once decoded.</exception>
    public AesGcmEncryptionService(string base64Key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Key);

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Encryption key must be valid base64.", nameof(base64Key), ex);
        }

        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException(
                $"Encryption key must decode to {KeySizeBytes} bytes (AES-256); got {key.Length}.",
                nameof(base64Key));
        }

        _key = key;
    }

    /// <inheritdoc/>
    public string Encrypt(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using (var aes = new AesGcm(_key, TagSizeBytes))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var payload = new byte[NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, payload, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(cipherBytes, 0, payload, NonceSizeBytes + TagSizeBytes, cipherBytes.Length);

        return Convert.ToBase64String(payload);
    }

    /// <inheritdoc/>
    public string Decrypt(string cipherText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cipherText);

        var payload = Convert.FromBase64String(cipherText);
        if (payload.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Ciphertext payload is too short to contain a nonce and tag.");
        }

        var nonce = payload.AsSpan(0, NonceSizeBytes);
        var tag = payload.AsSpan(NonceSizeBytes, TagSizeBytes);
        var cipherBytes = payload.AsSpan(NonceSizeBytes + TagSizeBytes);
        var plainBytes = new byte[cipherBytes.Length];

        using (var aes = new AesGcm(_key, TagSizeBytes))
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }
}
