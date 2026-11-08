using Maran.SharedKernel.Interfaces;

namespace Maran.SharedKernel.Security;

/// <summary>
/// The <see cref="IEncryptionService"/> every module's <c>DesignTimeDbContextFactory</c> hands to its
/// context. Exists only so EF Core's design-time tooling can build the MODEL — the shape of the
/// tables — which it cannot do without constructing the context.
/// </summary>
/// <remarks>
/// It does not encrypt, and it must not pretend to: a migration describes a text column, and which
/// cipher fills that column at runtime changes nothing about the DDL. A stub that returned its input
/// unchanged would work perfectly in the tooling and would silently write plaintext secrets if it
/// were ever registered at runtime by mistake; this one cannot be mistaken for a cipher because it
/// never produces a value. The alternative — giving the tooling a real key from configuration —
/// would put a working cipher, and therefore a decryption oracle, behind a type whose whole purpose
/// is to run on a developer's laptop with no database. Nothing here ever runs at runtime: the Host
/// registers the real <see cref="IEncryptionService"/> through DI, and no value is ever read or
/// written through this one.
/// </remarks>
public sealed class DesignTimeEncryptionService : IEncryptionService
{
    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always. Design-time tooling builds the model and never encrypts a value.</exception>
    public string Encrypt(string plainText)
    {
        throw new NotSupportedException("Design-time tooling builds the model and never encrypts a value.");
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always. Design-time tooling builds the model and never decrypts a value.</exception>
    public string Decrypt(string cipherText)
    {
        throw new NotSupportedException("Design-time tooling builds the model and never decrypts a value.");
    }
}
