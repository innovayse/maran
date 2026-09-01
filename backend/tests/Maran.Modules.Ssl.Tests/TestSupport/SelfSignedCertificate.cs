using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// Produces a real, parseable certificate for the fake authority to hand back, with a known expiry.
/// </summary>
/// <remarks>
/// A hand-written PEM string will not do: <c>AcmeClient.Materialize</c> parses the chain with
/// <c>X509Certificate2.CreateFromPem</c> and reads <c>NotAfter</c> off it, so a test that faked the
/// PEM would exercise the failure branch and never the one that matters.
/// </remarks>
public static class SelfSignedCertificate
{
    /// <summary>Builds a certificate expiring at <paramref name="notAfter"/>, PEM-encoded.</summary>
    /// <param name="notAfter">When the certificate should expire.</param>
    /// <returns>The PEM text.</returns>
    public static string PemExpiringAt(DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=example.com", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(notAfter.AddYears(-1), notAfter);

        return certificate.ExportCertificatePem();
    }
}
