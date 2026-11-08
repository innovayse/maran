using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Maran.Agent.Client.Channels;

/// <summary>
/// The one judgement of whether the certificate a node presents is the one an administrator approved.
/// </summary>
/// <remarks>
/// <para>
/// Trust here is a pin, not a chain: there is no certificate authority, so the TLS stack's own chain
/// and host-name verdicts are meaningless for a node (its certificate is self-signed and names whatever
/// address it was enrolled under) and are deliberately not consulted. What replaces them is the
/// SHA-256 of the certificate's DER encoding, compared in constant time.
/// </para>
/// <para>
/// A matching fingerprint alone is not enough: returning <c>true</c> on the hash match would ignore the
/// validity period, and an expired node certificate would keep working silently although the panel
/// promises to show and alert on expiry. So the validity window is part of the same judgement.
/// </para>
/// </remarks>
internal static class PinnedCertificateCheck
{
    /// <summary>Whether <paramref name="presented"/> is the pinned certificate and is valid at <paramref name="now"/>.</summary>
    /// <param name="presented">The certificate the node sent; <c>null</c> when it sent none.</param>
    /// <param name="pinnedFingerprint">SHA-256 of the approved certificate's DER encoding, 64 lowercase hex digits.</param>
    /// <param name="now">The current instant, from the panel's clock.</param>
    /// <returns><c>true</c> only for exactly the pinned certificate inside its validity period.</returns>
    public static bool Accepts(X509Certificate? presented, string pinnedFingerprint, DateTimeOffset now)
    {
        if (presented is null)
        {
            return false;
        }

        var presentedFingerprint = Convert.ToHexStringLower(SHA256.HashData(presented.GetRawCertData()));
        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(presentedFingerprint),
            Encoding.ASCII.GetBytes(pinnedFingerprint));
        if (!matches)
        {
            return false;
        }

        using var certificate = new X509Certificate2(presented);
        return now >= new DateTimeOffset(certificate.NotBefore) && now <= new DateTimeOffset(certificate.NotAfter);
    }
}
