namespace Maran.Modules.Licensing.Domain.ValueObjects;

/// <summary>
/// The one Ed25519 public key a lease is checked against, or the fact that this build has none. A type
/// of its own and not a second <see cref="LicenceVerificationKey"/>: the lease key is deliberately a
/// different key (decision D3), so that a leaked lease key cannot mint licences and a leaked licence
/// key cannot mint leases, and a signature check that is handed the wrong one must not compile.
/// </summary>
/// <remarks>
/// Like the licence key, "no key" is a value and never a placeholder: a build with no lease key refuses
/// every lease (<see cref="Maran.Sdk.Contracts.LeaseRefusalReason.NoVerificationKey"/>), and no key is
/// ever a constant in source, so no build can trust a key whose private half is public.
/// </remarks>
public sealed class LeaseVerificationKey
{
    /// <summary>The length of an Ed25519 public key, in bytes.</summary>
    public const int PublicKeyLength = 32;

    /// <summary>The raw public key, or <see langword="null"/> when this build trusts no lease key.</summary>
    private readonly byte[]? _publicKey;

    /// <summary>The state in which no lease can verify, because there is no key to verify it against.</summary>
    public static LeaseVerificationKey None { get; } = new(null);

    /// <summary>Whether a key is configured.</summary>
    public bool IsConfigured
    {
        get { return _publicKey is not null; }
    }

    /// <summary>The raw 32-byte public key.</summary>
    /// <exception cref="InvalidOperationException">No key is configured; check <see cref="IsConfigured"/> first.</exception>
    public byte[] PublicKey
    {
        get
        {
            return _publicKey is null
                ? throw new InvalidOperationException("No lease verification key is configured.")
                : (byte[])_publicKey.Clone();
        }
    }

    /// <summary>Creates the key value.</summary>
    /// <param name="publicKey">The raw public key, or <see langword="null"/> for none.</param>
    private LeaseVerificationKey(byte[]? publicKey)
    {
        _publicKey = publicKey;
    }

    /// <summary>Wraps a raw Ed25519 public key.</summary>
    /// <param name="publicKey">The raw 32-byte public key.</param>
    /// <returns>The configured key, or <see cref="None"/> when the bytes are not an Ed25519 public key's length.</returns>
    public static LeaseVerificationKey Of(byte[] publicKey)
    {
        return publicKey.Length == PublicKeyLength ? new LeaseVerificationKey((byte[])publicKey.Clone()) : None;
    }
}
