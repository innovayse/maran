using Maran.SharedKernel.Interfaces;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The cipher handed to a module's <c>DbContext</c> while its MODEL is being built, and nothing
/// more.
/// </summary>
/// <remarks>
/// A context that encrypts a column takes <see cref="IEncryptionService"/> so it can attach a value
/// converter, and the converter is CONSTRUCTED during model building even though nothing converts a
/// value. So this must exist and must never be called: the identity behaviour below would be a
/// grave defect in production and is unreachable here, which is why it lives in the architecture
/// tests rather than anywhere a module could resolve it.
/// </remarks>
public sealed class ArchitectureEncryptionService : IEncryptionService
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
