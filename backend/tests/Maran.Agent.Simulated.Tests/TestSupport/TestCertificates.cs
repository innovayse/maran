using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Maran.Agent.Simulated.Tests.TestSupport;

/// <summary>Issues throwaway self-signed certificates for tests; nothing is persisted or sent anywhere.</summary>
public static class TestCertificates
{
    /// <summary>Issues a certificate for an RSA key.</summary>
    /// <param name="notAfter">When it should expire; rounded down to the second.</param>
    /// <returns>The certificate and its key.</returns>
    public static TestCertificateMaterial Rsa(DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=demo.example.test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return Issue(request, key.ExportPkcs8PrivateKeyPem(), notAfter);
    }

    /// <summary>Issues a certificate for an elliptic-curve key.</summary>
    /// <param name="notAfter">When it should expire; rounded down to the second.</param>
    /// <returns>The certificate and its key.</returns>
    public static TestCertificateMaterial EllipticCurve(DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=demo.example.test", key, HashAlgorithmName.SHA256);

        return Issue(request, key.ExportPkcs8PrivateKeyPem(), notAfter);
    }

    /// <summary>Signs <paramref name="request"/> with its own key, valid for ninety days before the expiry.</summary>
    /// <param name="request">The certificate request.</param>
    /// <param name="privateKeyPem">The PEM of the key the request was built over.</param>
    /// <param name="notAfter">When the certificate should expire.</param>
    /// <returns>The certificate and its key.</returns>
    private static TestCertificateMaterial Issue(
        CertificateRequest request,
        string privateKeyPem,
        DateTimeOffset notAfter)
    {
        var expiry = DateTimeOffset.FromUnixTimeSeconds(notAfter.ToUnixTimeSeconds());
        using var certificate = request.CreateSelfSigned(expiry.AddDays(-90), expiry);

        return new TestCertificateMaterial(certificate.ExportCertificatePem(), privateKeyPem, expiry);
    }
}
