using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Models;
using Maran.SharedKernel.Results;

namespace Maran.Host.Demo.Adapters;

/// <summary>
/// The demo's <see cref="IAcmeClient"/>: it "issues" a short-lived self-signed certificate from a fresh
/// throwaway key, entirely in memory. It contacts no certificate authority, creates no HTTP client and
/// writes no challenge file (threat note T4).
/// </summary>
/// <remarks>
/// A real order would need a public domain that answers HTTP-01, which nobody serves for a visitor-typed
/// name, so a test authority cannot stand in either. The certificate is valid for
/// <see cref="Lifetime"/>, so the expiring and renewing states the screens show are reachable.
/// </remarks>
public sealed class SimulatedAcmeClient : IAcmeClient
{
    /// <summary>How long a simulated certificate is valid.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>How far before now a certificate becomes valid, to absorb clock differences.</summary>
    private static readonly TimeSpan Backdate = TimeSpan.FromMinutes(5);

    /// <summary>The time source.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the client.</summary>
    /// <param name="clock">The time source.</param>
    public SimulatedAcmeClient(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>Signs a certificate for the domain with a key made for this one call.</summary>
    /// <param name="request">The domain to order for; the challenge target is ignored, nothing is proven.</param>
    /// <param name="cancellationToken">Cancellation; unused, nothing here waits.</param>
    /// <returns>The certificate and its private key as PEM, and when it expires.</returns>
    public Task<Result<IssuedCertificate>> OrderAsync(AcmeOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        var notAfter = now + Lifetime;

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certificateRequest = new CertificateRequest(
            new X500DistinguishedName("CN=maran-demo"), key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(request.Domain);
        certificateRequest.CertificateExtensions.Add(names.Build());

        using var certificate = certificateRequest.CreateSelfSigned(now - Backdate, notAfter);
        var issued = new IssuedCertificate(
            certificate.ExportCertificatePem(),
            key.ExportPkcs8PrivateKeyPem(),
            notAfter);
        return Task.FromResult(Result<IssuedCertificate>.Ok(issued));
    }
}
