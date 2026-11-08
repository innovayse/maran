namespace Maran.Agent.Simulated.Tests.TestSupport;

/// <summary>A certificate and the private key it was issued for, as PEM text.</summary>
/// <param name="CertificatePem">The PEM-encoded certificate.</param>
/// <param name="PrivateKeyPem">The PEM-encoded PKCS#8 private key.</param>
/// <param name="NotAfter">When the certificate stops being valid, to the second.</param>
public sealed record TestCertificateMaterial(string CertificatePem, string PrivateKeyPem, DateTimeOffset NotAfter);
