using Maran.SharedKernel.Interfaces;

namespace Maran.SharedKernel.Security;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>
/// EF Core value converter that transparently encrypts a <see cref="string"/> column at rest.
/// A module's <c>&lt;Entity&gt;Configuration.cs</c> applies it per property:
/// <c>builder.Property(e =&gt; e.ApiKey).HasConversion(new EncryptedStringConverter(encryptionService));</c>
/// so callers read and write plain text while PostgreSQL only ever stores ciphertext.
/// </summary>
public sealed class EncryptedStringConverter : ValueConverter<string, string>
{
    /// <summary>Creates the converter backed by <paramref name="encryptionService"/>.</summary>
    /// <param name="encryptionService">The cipher used to encrypt on write and decrypt on read.</param>
    public EncryptedStringConverter(IEncryptionService encryptionService)
        : base(
            plainText => encryptionService.Encrypt(plainText),
            cipherText => encryptionService.Decrypt(cipherText))
    {
    }
}
