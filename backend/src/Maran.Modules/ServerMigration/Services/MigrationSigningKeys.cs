using System.Security.Cryptography;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Persistence;
using Maran.SharedKernel.Security;
using Maran.SharedKernel.Utilities.Signatures;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Owns this server's package-signing key: makes it on first use, hands it to the services that sign, and signs
/// the panel's header (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// ECDSA P-384 with SHA-384, the scheme the agent verifies and the BCL provides, so no dependency is added
/// (rules/security.md item 9). The key is made when the first export needs it and never replaced: replacing it
/// would silently invalidate the fingerprint every operator has already confirmed against this server.
/// </para>
/// <para>
/// The private half leaves this type only as a <see cref="SensitiveString"/>, to be handed to the agent for one
/// export call and to sign a header here.
/// </para>
/// </remarks>
public sealed class MigrationSigningKeys
{
    /// <summary>The module's database context.</summary>
    private readonly ServerMigrationDbContext _dbContext;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    public MigrationSigningKeys(ServerMigrationDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Reads this server's key, making it first when there is none.</summary>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>The key, whose private half is masked in any formatting of it.</returns>
    public async Task<ServerSigningKey> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var stored = await _dbContext.MigrationSigningKeys.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            stored = Generate();
            _dbContext.MigrationSigningKeys.Add(stored);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ServerSigningKey(
            new SensitiveString(stored.PrivateKey),
            Convert.FromBase64String(stored.PublicKey),
            stored.Fingerprint);
    }

    /// <summary>Signs bytes with the key.</summary>
    /// <param name="key">This server's key.</param>
    /// <param name="data">The exact bytes to sign.</param>
    /// <returns>The ASN.1 DER signature (RFC 3279), which is what the agent and the reader verify.</returns>
    public static byte[] Sign(ServerSigningKey key, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(key);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(key.PrivateKey.Reveal()), out _);

        return ecdsa.SignData(data, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence);
    }

    /// <summary>Makes a new P-384 key pair.</summary>
    /// <returns>The row to store.</returns>
    private MigrationSigningKey Generate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
        var point = new byte[EcdsaP384Signature.UncompressedPointBytes];
        point[0] = 0x04;
        parameters.Q.X!.CopyTo(point, 1);
        parameters.Q.Y!.CopyTo(point, 49);

        return new MigrationSigningKey(
            Guid.NewGuid(),
            Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()),
            Convert.ToBase64String(point),
            PublicKeyFingerprint.Of(point),
            _clock.UtcNow);
    }
}
