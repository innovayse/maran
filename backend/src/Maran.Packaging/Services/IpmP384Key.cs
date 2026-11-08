using System.Security.Cryptography;

namespace Maran.Packaging.Services;

/// <summary>
/// Whether a key is on the NIST P-384 curve. Asked by the curve itself and not by the key size,
/// because another curve (Brainpool P-384, for one) is also 384 bits and a signature scheme must not
/// accept a key from a curve its designers did not review.
/// </summary>
internal static class IpmP384Key
{
    /// <summary>The OID of the NIST P-384 curve.</summary>
    private const string Oid = "1.3.132.0.34";

    /// <summary>The names the platform may report for the curve when it fills in a friendly name and no OID value.</summary>
    private static readonly string[] FriendlyNames = ["nistP384", "secp384r1", "ECDSA_P384"];

    /// <summary>Reads the key's curve.</summary>
    /// <param name="key">The key to inspect.</param>
    /// <returns>True when the key is on NIST P-384.</returns>
    public static bool Is(ECDsa key)
    {
        var curve = key.ExportParameters(false).Curve.Oid;

        return string.Equals(curve.Value, Oid, StringComparison.Ordinal)
            || (curve.FriendlyName is not null
                && FriendlyNames.Contains(curve.FriendlyName, StringComparer.OrdinalIgnoreCase));
    }
}
