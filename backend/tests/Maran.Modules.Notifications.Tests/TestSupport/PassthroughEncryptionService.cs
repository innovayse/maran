using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Notifications.Tests.TestSupport;

/// <summary>
/// An <see cref="IEncryptionService"/> that returns what it is given, so the in-memory provider can
/// round-trip the SMTP password column without a key.
/// </summary>
/// <remarks>
/// What these tests assert about the password is that it never leaves the module through a READ
/// MODEL, which is a property of the DTO's shape and of the query handler — not of the cipher. Using
/// the real AES-GCM service here would need a key in a test file and would prove nothing extra.
/// </remarks>
public sealed class PassthroughEncryptionService : IEncryptionService
{
    /// <summary>Returns the plain text unchanged.</summary>
    /// <param name="plainText">The value to "encrypt".</param>
    /// <returns>The same value.</returns>
    public string Encrypt(string plainText)
    {
        return plainText;
    }

    /// <summary>Returns the cipher text unchanged.</summary>
    /// <param name="cipherText">The value to "decrypt".</param>
    /// <returns>The same value.</returns>
    public string Decrypt(string cipherText)
    {
        return cipherText;
    }
}
