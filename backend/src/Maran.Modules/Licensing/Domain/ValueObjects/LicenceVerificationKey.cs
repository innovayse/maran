namespace Maran.Modules.Licensing.Domain.ValueObjects;

/// <summary>
/// The one Ed25519 public key a verifier trusts, or the fact that this build has none.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why "no key" is a value and not a null or a placeholder.</b> A verifier that always holds
/// SOME key invites the build to ship one before the real key exists, and a placeholder whose
/// private half is readable by anyone lets anyone mint a licence that verifies. So the absence of a
/// key is a first-class state: a verifier built over <see cref="None"/> refuses every licence, and
/// the verifier says why (<see cref="Enums.LicenceRefusalReason.NoVerificationKey"/>) instead of
/// pretending a signature failed.
/// </para>
/// <para>
/// The key is never a constant in source. The release supplies it (see
/// <see cref="Services.EmbeddedLicenceVerificationKeySource"/>); a test supplies its own through the
/// verifier's constructor, so no test key is ever the production key.
/// </para>
/// </remarks>
public sealed class LicenceVerificationKey
{
    /// <summary>The length of an Ed25519 public key, in bytes.</summary>
    public const int PublicKeyLength = 32;

    /// <summary>The raw public key, or <see langword="null"/> when this build trusts no key.</summary>
    private readonly byte[]? _publicKey;

    /// <summary>The state in which no licence can verify, because there is no key to verify it against.</summary>
    public static LicenceVerificationKey None { get; } = new(null);

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
                ? throw new InvalidOperationException("No licence verification key is configured.")
                : (byte[])_publicKey.Clone();
        }
    }

    /// <summary>Creates the key value.</summary>
    /// <param name="publicKey">The raw public key, or <see langword="null"/> for none.</param>
    private LicenceVerificationKey(byte[]? publicKey)
    {
        _publicKey = publicKey;
    }

    /// <summary>Wraps a raw Ed25519 public key.</summary>
    /// <param name="publicKey">The raw 32-byte public key.</param>
    /// <returns>The configured key, or <see cref="None"/> when the bytes are not an Ed25519 public key's length.</returns>
    public static LicenceVerificationKey Of(byte[] publicKey)
    {
        return publicKey.Length == PublicKeyLength ? new LicenceVerificationKey((byte[])publicKey.Clone()) : None;
    }
}
