using System.Security.Cryptography;

namespace Maran.SharedKernel.Utilities.Signatures;

/// <summary>
/// The one implementation of "is this a valid ECDSA P-384 / SHA-384 signature": the scheme the release
/// manifest, the module packages and the marketplace's catalogue are all signed with. Pure: key bytes
/// and data in, an answer out, nothing thrown for a hostile input. Two verifiers calling this cannot
/// drift apart on the curve check or the signature framing.
/// </summary>
/// <remarks>
/// The signature is DER encoded (RFC 3279 <c>ECDSA-Sig-Value</c>), which is exactly what
/// <c>openssl dgst -sha384 -sign</c> writes. A key on another 384-bit curve is refused: a signature
/// scheme must not accept a key from a curve its designers did not review. The scheme is separate from
/// the licence's Ed25519 (rules/security.md item 9), and a key trusted for one role must not sign in
/// another, which is why this type takes the key as an argument and carries no default.
/// </remarks>
public static class EcdsaP384Signature
{
    /// <summary>The longest DER signature that can be valid for P-384 (two 48-byte integers and their framing).</summary>
    public const int MaxSignatureBytes = 104;

    /// <summary>The OID of the NIST P-384 curve.</summary>
    private const string CurveOid = "1.3.132.0.34";

    /// <summary>The names the platform may report for the curve when it fills in a friendly name and no OID value.</summary>
    private static readonly string[] FriendlyNames = ["nistP384", "secp384r1", "ECDSA_P384"];

    /// <summary>Whether a key is on the NIST P-384 curve, asked of the curve and not of the key size.</summary>
    /// <param name="key">The key to inspect.</param>
    /// <returns>True when the key is on NIST P-384.</returns>
    public static bool IsP384(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var curve = key.ExportParameters(false).Curve.Oid;

        return string.Equals(curve.Value, CurveOid, StringComparison.Ordinal)
            || (curve.FriendlyName is not null
                && FriendlyNames.Contains(curve.FriendlyName, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Reads a PEM <c>PUBLIC KEY</c> block into the key as SubjectPublicKeyInfo, refusing any key not on P-384.</summary>
    /// <param name="pem">The PEM text, the form <c>installer/keys/*.pub</c> uses.</param>
    /// <returns>The key as SubjectPublicKeyInfo bytes, or null when the text is not a P-384 public key.</returns>
    public static byte[]? TryReadPem(string pem)
    {
        ArgumentNullException.ThrowIfNull(pem);

        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            return IsP384(key) ? key.ExportSubjectPublicKeyInfo() : null;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Verifies a DER-encoded signature over <paramref name="data"/>.</summary>
    /// <param name="publicKey">The signer's key as SubjectPublicKeyInfo, on curve P-384.</param>
    /// <param name="data">The exact bytes that were signed.</param>
    /// <param name="signature">The DER-encoded signature.</param>
    /// <returns>True only for a valid signature by that key; false for anything else, a malformed key or signature included.</returns>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (signature.IsEmpty || signature.Length > MaxSignatureBytes)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out _);

            if (!IsP384(key))
            {
                return false;
            }

            return key.VerifyData(data, signature, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>The length of an uncompressed SEC1 point on P-384: <c>0x04 || X || Y</c>.</summary>
    public const int UncompressedPointBytes = 97;

    /// <summary>Verifies a DER-encoded signature by a key given as an uncompressed SEC1 point.</summary>
    /// <param name="point">The signer's key as <c>0x04 || X || Y</c> (97 bytes), as the agent states it.</param>
    /// <param name="data">The exact bytes that were signed.</param>
    /// <param name="signature">The DER-encoded signature.</param>
    /// <returns>True only for a valid signature by a key that is on P-384; false for anything else, a malformed point included.</returns>
    public static bool VerifyUncompressedPoint(
        ReadOnlySpan<byte> point,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> signature)
    {
        if (point.Length != UncompressedPointBytes || point[0] != 0x04)
        {
            return false;
        }

        try
        {
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP384,
                Q = new ECPoint { X = point.Slice(1, 48).ToArray(), Y = point.Slice(49, 48).ToArray() },
            };
            using var key = ECDsa.Create(parameters);

            return Verify(key.ExportSubjectPublicKeyInfo(), data, signature);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
