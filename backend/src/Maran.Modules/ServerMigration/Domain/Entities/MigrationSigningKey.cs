namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>
/// The per-server key that signs what this server exports (issue #127): an ECDSA P-384 pair, the private half
/// encrypted at rest.
/// </summary>
/// <remarks>
/// <para>
/// There is one row. The public half and its SHA-256 fingerprint are what an operator on a receiving server
/// compares against this server before an import is accepted; a valid signature proves only that the holder of
/// this key signed, and the fingerprint is how the operator knows the holder is this server.
/// </para>
/// <para>
/// The private half is a secret in the sense of rules/security.md item 8: it is stored encrypted, handed to the
/// agent for the length of one export call, and appears in no DTO, log or message. It is never changed after
/// creation, so there is no method that does.
/// </para>
/// </remarks>
public sealed class MigrationSigningKey
{
    /// <summary>The longest fingerprint: the hexadecimal SHA-256 of the public key.</summary>
    public const int FingerprintLength = 64;

    /// <summary>The longest base64 text of the public key: an uncompressed SEC1 point of 97 bytes.</summary>
    public const int PublicKeyMaxLength = 256;

    /// <summary>The row's id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The PKCS#8 DER private key, base64-encoded; encrypted in the column.</summary>
    public string PrivateKey { get; private set; }

    /// <summary>The uncompressed SEC1 public point, base64-encoded.</summary>
    public string PublicKey { get; private set; }

    /// <summary>The lowercase hexadecimal SHA-256 of the public point, which an operator compares.</summary>
    public string Fingerprint { get; private set; }

    /// <summary>When the key was made.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records a newly generated key pair.</summary>
    /// <param name="id">The row's id.</param>
    /// <param name="privateKey">The PKCS#8 DER private key, base64-encoded.</param>
    /// <param name="publicKey">The uncompressed SEC1 public point, base64-encoded.</param>
    /// <param name="fingerprint">The lowercase hexadecimal SHA-256 of the public point.</param>
    /// <param name="createdAt">When the key was made.</param>
    public MigrationSigningKey(Guid id, string privateKey, string publicKey, string fingerprint, DateTimeOffset createdAt)
    {
        Id = id;
        PrivateKey = privateKey;
        PublicKey = publicKey;
        Fingerprint = fingerprint;
        CreatedAt = createdAt;
    }

    /// <summary>For EF Core only.</summary>
    private MigrationSigningKey()
    {
        PrivateKey = string.Empty;
        PublicKey = string.Empty;
        Fingerprint = string.Empty;
    }
}
