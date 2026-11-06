using System.Security.Cryptography;
using System.Text;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Security;

/// <summary>
/// HMAC-SHA256 implementation of <see cref="IIdempotencyFingerprint"/>. The MAC key is not the panel's
/// encryption key itself but a subkey derived from it with HKDF-SHA256 under a fixed, domain-separated
/// label, so the fingerprint shares no key material with the cipher that protects secrets at rest and
/// learning one never yields the other.
/// </summary>
/// <remarks>
/// It lives in the Host and not in <c>SharedKernel/Security/</c>, although it holds a key, because the
/// contract it implements is the Sdk's and the kernel references nothing of ours. A new label
/// (<c>-v2</c>) is how the derivation changes: every stored fingerprint then simply stops matching, and
/// a retry within the replay window of such a release runs again instead of replaying, which is the
/// safe direction.
/// </remarks>
public sealed class HmacIdempotencyFingerprint : IIdempotencyFingerprint
{
    /// <summary>The HKDF info label; it separates this subkey from every other use of the panel's secret.</summary>
    public const string DerivationLabel = "maran-idempotency-fingerprint-v1";

    /// <summary>The derived MAC key, in bytes.</summary>
    private const int DerivedKeyBytes = 32;

    /// <summary>The key the HMAC runs under, derived once at construction.</summary>
    private readonly byte[] _macKey;

    /// <summary>Creates the fingerprint from the panel's base64 encryption key.</summary>
    /// <param name="base64SecretKey">The panel's secret key, base64-encoded; the MAC key is derived from it.</param>
    /// <exception cref="ArgumentException">The key is missing or not valid base64.</exception>
    public HmacIdempotencyFingerprint(string base64SecretKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64SecretKey);

        byte[] secret;
        try
        {
            secret = Convert.FromBase64String(base64SecretKey);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The secret key must be valid base64.", nameof(base64SecretKey), exception);
        }

        _macKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256, secret, DerivedKeyBytes, salt: [], info: Encoding.UTF8.GetBytes(DerivationLabel));
    }

    /// <inheritdoc />
    public string Compute(byte[] requestMaterial)
    {
        ArgumentNullException.ThrowIfNull(requestMaterial);

        return Convert.ToHexString(HMACSHA256.HashData(_macKey, requestMaterial)).ToLowerInvariant();
    }
}
