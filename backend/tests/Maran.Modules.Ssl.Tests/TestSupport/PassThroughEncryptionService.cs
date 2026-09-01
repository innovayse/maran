using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IEncryptionService"/> double that stores what it is given. Only the ACME account
/// key column is encrypted, and no test here asserts on ciphertext — what the tests need is a
/// context that constructs, not a cipher.
/// </summary>
public sealed class PassThroughEncryptionService : IEncryptionService
{
    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        return plainText;
    }

    /// <inheritdoc />
    public string Decrypt(string cipherText)
    {
        return cipherText;
    }
}
