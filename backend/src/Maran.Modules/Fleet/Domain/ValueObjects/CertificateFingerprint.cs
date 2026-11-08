using System.Security.Cryptography;
using System.Text;

namespace Maran.Modules.Fleet.Domain.ValueObjects;

/// <summary>
/// The SHA-256 digest of a certificate's DER encoding, spelled as 64 lowercase hexadecimal digits: the
/// one identity a node has for this panel and this panel has for a node. There is no certificate
/// authority; trust is exactly this value, pinned on each side.
/// </summary>
/// <remarks>
/// <para>
/// Comparison is constant-time (<see cref="Matches"/>). The values are public fingerprints, not
/// secrets, so the timing of a mismatch discloses nothing the peer did not already hold; the
/// constant-time compare is here because the same type is the comparison a pin check is written
/// against, and a pin check that short-circuits is the shape of defect a reviewer should never have to
/// weigh.
/// </para>
/// <para>
/// The text an administrator types is parsed into this type before it is compared, and parsing is
/// strict about the form the node prints: exactly 64 hexadecimal digits, case-insensitive on input
/// (a terminal may be upper-cased by a reader), always lowercase once held. Colon-separated groups,
/// spaces and a <c>sha256:</c> prefix are refused rather than guessed at, so the value that is stored
/// and the value that is compared are one spelling.
/// </para>
/// </remarks>
public sealed class CertificateFingerprint
{
    /// <summary>The number of hexadecimal digits in a SHA-256 digest.</summary>
    public const int Length = 64;

    /// <summary>The fingerprint as 64 lowercase hexadecimal digits.</summary>
    public string Value { get; }

    /// <summary>Holds an already-normalised value; the only callers are the two factories below.</summary>
    /// <param name="value">64 lowercase hexadecimal digits.</param>
    private CertificateFingerprint(string value)
    {
        Value = value;
    }

    /// <summary>Reads a fingerprint from the text an administrator typed or a node printed.</summary>
    /// <param name="text">The candidate: exactly 64 hexadecimal digits.</param>
    /// <param name="fingerprint">The parsed value when the text is acceptable; otherwise null.</param>
    /// <returns>True when <paramref name="text"/> is a fingerprint; false otherwise, without throwing.</returns>
    public static bool TryParse(string? text, out CertificateFingerprint? fingerprint)
    {
        fingerprint = null;

        if (text is null || text.Length != Length)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        fingerprint = new CertificateFingerprint(text.ToLowerInvariant());
        return true;
    }

    /// <summary>Computes the fingerprint of a certificate's DER bytes.</summary>
    /// <param name="certificateDer">The DER encoding of the certificate; not empty.</param>
    /// <returns>The SHA-256 fingerprint of those bytes.</returns>
    public static CertificateFingerprint OfDer(ReadOnlySpan<byte> certificateDer)
    {
        if (certificateDer.IsEmpty)
        {
            throw new ArgumentException("A certificate has at least one byte.", nameof(certificateDer));
        }

        var digest = SHA256.HashData(certificateDer);
        return new CertificateFingerprint(Convert.ToHexStringLower(digest));
    }

    /// <summary>Compares two fingerprints without stopping at the first differing digit.</summary>
    /// <param name="other">The fingerprint to compare with.</param>
    /// <returns>True when both hold the same digest.</returns>
    public bool Matches(CertificateFingerprint other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Value),
            Encoding.ASCII.GetBytes(other.Value));
    }

    /// <summary>The 64 lowercase digits, the form that is stored and shown.</summary>
    /// <returns>The fingerprint's text.</returns>
    public override string ToString()
    {
        return Value;
    }
}
