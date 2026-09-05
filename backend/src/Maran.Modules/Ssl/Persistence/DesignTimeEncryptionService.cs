namespace Maran.Modules.Ssl.Persistence;

/// <summary>
/// The <see cref="IEncryptionService"/> handed to <see cref="SslDbContext"/> by
/// <see cref="DesignTimeDbContextFactory"/>. Exists only so EF Core's design-time tooling can build
/// the MODEL — the shape of the tables — which it cannot do without constructing the context.
/// </summary>
/// <remarks>
/// It does not encrypt, and it must not pretend to: a migration describes a text column, and which
/// cipher fills that column at runtime changes nothing about the DDL. The alternative — giving the
/// tooling a real key from configuration — would put a working cipher, and therefore a decryption
/// oracle, behind a type whose whole purpose is to run on a developer's laptop with no database.
/// Nothing here ever runs at runtime: the Host registers the real <c>IEncryptionService</c> through
/// DI, and no value is ever read or written through this one.
/// </remarks>
public sealed class DesignTimeEncryptionService : IEncryptionService
{
    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always. Design-time tooling never encrypts a value.</exception>
    public string Encrypt(string plainText)
    {
        throw new NotSupportedException("Design-time tooling builds the model and never encrypts a value.");
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always. Design-time tooling never decrypts a value.</exception>
    public string Decrypt(string cipherText)
    {
        throw new NotSupportedException("Design-time tooling builds the model and never decrypts a value.");
    }
}
