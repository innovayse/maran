using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Maran.Agent.Client.Tests.TestSupport;

/// <summary>Self-signed ECDSA P-256 certificates with a chosen validity, the shape fleet identities have.</summary>
internal static class TestCertificates
{
    /// <summary>The instant every certificate and clock in these tests is anchored to.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Creates a self-signed certificate with its private key.</summary>
    /// <param name="commonName">The subject's common name; two certificates may share it and still differ by key.</param>
    /// <param name="notBefore">Start of the validity period.</param>
    /// <param name="notAfter">End of the validity period.</param>
    /// <returns>The certificate, loaded so that its key is usable by the TLS stack.</returns>
    public static X509Certificate2 Create(string commonName, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
        using var created = request.CreateSelfSigned(notBefore, notAfter);
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
    }

    /// <summary>Creates a certificate valid for a year around <see cref="Now"/>.</summary>
    /// <param name="commonName">The subject's common name.</param>
    /// <returns>The certificate.</returns>
    public static X509Certificate2 CreateValid(string commonName)
    {
        return Create(commonName, Now.AddDays(-1), Now.AddDays(365));
    }

    /// <summary>The pin form of a certificate: SHA-256 of its DER encoding, lowercase hex.</summary>
    /// <param name="certificate">The certificate to fingerprint.</param>
    /// <returns>64 lowercase hex digits.</returns>
    public static string Fingerprint(X509Certificate certificate)
    {
        return Convert.ToHexStringLower(SHA256.HashData(certificate.GetRawCertData()));
    }
}
