using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Maran.Modules.Ssl.Services;

/// <summary>Makes the certificate key and the signing request that asks for a domain.</summary>
internal static class AcmeCertificateRequest
{
    /// <summary>Generates a fresh certificate key and the CSR that asks for the given names.</summary>
    /// <param name="names">The names to request: the first is the subject, and every one is a subject alternative name.</param>
    /// <param name="key">The generated key, which the caller owns and must dispose.</param>
    /// <returns>The DER-encoded certificate signing request.</returns>
    /// <remarks>
    /// A NEW key on every issuance, including every renewal. Reusing a key across renewals means one
    /// compromise never expires; generating one is a few milliseconds, and the old key is discarded
    /// with the old certificate.
    /// </remarks>
    public static byte[] Create(IReadOnlyList<string> names, out ECDsa key)
    {
        var created = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        try
        {
            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            foreach (var name in names)
            {
                subjectAlternativeNames.AddDnsName(name);
            }

            var csr = new CertificateRequest($"CN={names[0]}", created, HashAlgorithmName.SHA256);
            csr.CertificateExtensions.Add(subjectAlternativeNames.Build());
            var request = csr.CreateSigningRequest();

            key = created;
            return request;
        }
        catch
        {
            // The out parameter is assigned only on the success path, so a throw here leaves the
            // caller with nothing to dispose — which is why it has to be disposed here.
            created.Dispose();
            throw;
        }
    }
}
