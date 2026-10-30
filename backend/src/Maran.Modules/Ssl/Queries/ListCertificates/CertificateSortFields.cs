namespace Maran.Modules.Ssl.Queries.ListCertificates;

/// <summary>
/// The public names the certificates list sorts by — the <c>sort</c> parameter's vocabulary, written
/// once and read by <see cref="ListCertificatesSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class CertificateSortFields
{
    /// <summary>When the certificate expires.</summary>
    public const string NotAfter = "notAfter";

    /// <summary>The domain the certificate was issued for.</summary>
    public const string Domain = "domain";
}
