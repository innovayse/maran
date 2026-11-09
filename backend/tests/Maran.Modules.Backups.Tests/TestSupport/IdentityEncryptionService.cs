using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>A cipher that returns what it is given, for reading an encrypted column as it is stored.</summary>
/// <remarks>
/// It protects nothing. It is the control of <see cref="MarkingEncryptionService"/>: the two together
/// separate "the value was stored encrypted" from "the value round-tripped".
/// </remarks>
public sealed class IdentityEncryptionService : IEncryptionService
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
