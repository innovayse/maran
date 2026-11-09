using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>An <see cref="IEncryptionService"/> that wraps text in a visible marker, so a test can tell stored ciphertext from plaintext.</summary>
public sealed class ReversibleEncryption : IEncryptionService
{
    /// <summary>The marker the ciphertext starts with.</summary>
    public const string Marker = "enc:";

    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        return Marker + plainText;
    }

    /// <inheritdoc />
    public string Decrypt(string cipherText)
    {
        return cipherText[Marker.Length..];
    }
}
