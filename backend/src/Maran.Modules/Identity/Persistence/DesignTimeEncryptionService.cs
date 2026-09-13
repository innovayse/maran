namespace Maran.Modules.Identity.Persistence;

/// <summary>
/// The <see cref="IEncryptionService"/> handed to <see cref="IdentityDbContext"/> by
/// <see cref="DesignTimeDbContextFactory"/>. Generating a migration asks the value converter only
/// for its type mapping, never to encrypt or decrypt anything, so this implementation refuses both
/// operations rather than pretending to perform them.
/// </summary>
/// <remarks>
/// Refusing loudly is the point. A stub that returned its input unchanged would work perfectly in
/// the tooling and would silently write plaintext secrets if it were ever registered at runtime by
/// mistake; this one cannot be mistaken for a cipher because it never produces a value.
/// </remarks>
public sealed class DesignTimeEncryptionService : IEncryptionService
{
    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        throw new NotSupportedException("The design-time encryption service cannot encrypt; it exists only for EF Core tooling.");
    }

    /// <inheritdoc />
    public string Decrypt(string cipherText)
    {
        throw new NotSupportedException("The design-time encryption service cannot decrypt; it exists only for EF Core tooling.");
    }
}
