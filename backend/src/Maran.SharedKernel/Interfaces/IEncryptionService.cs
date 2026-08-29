namespace Maran.SharedKernel.Interfaces;

/// <summary>
/// Encrypts and decrypts strings at rest — API keys, licence data, and other module secrets
/// stored in PostgreSQL (rules/security.md "Secrets"). Shared here, not per-module, so every
/// module encrypts sensitive columns the same way instead of rolling its own crypto.
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypts <paramref name="plainText"/> and returns a single self-contained, base64-encoded
    /// payload (nonce, authentication tag, and ciphertext) suitable for storing in a text column.
    /// </summary>
    /// <param name="plainText">The secret to encrypt.</param>
    string Encrypt(string plainText);

    /// <summary>
    /// Decrypts a payload produced by <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="cipherText">The base64 payload previously returned by <see cref="Encrypt"/>.</param>
    /// <returns>The original plain text.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The payload was truncated, corrupted, or tampered with — GCM's authentication tag failed to verify.
    /// </exception>
    string Decrypt(string cipherText);
}
