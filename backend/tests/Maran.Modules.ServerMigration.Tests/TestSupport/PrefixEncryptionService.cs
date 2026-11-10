using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>
/// A reversible stand-in for the panel's cipher that makes ciphertext recognisable: it prefixes the text, so a
/// test can tell a value that went through the cipher from one that was stored as it was.
/// </summary>
public sealed class PrefixEncryptionService : IEncryptionService
{
    /// <summary>What every ciphertext starts with.</summary>
    public const string Prefix = "enc:";

    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        return Prefix + plainText;
    }

    /// <inheritdoc />
    public string Decrypt(string cipherText)
    {
        if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Not a ciphertext of this cipher.");
        }

        return cipherText[Prefix.Length..];
    }
}
