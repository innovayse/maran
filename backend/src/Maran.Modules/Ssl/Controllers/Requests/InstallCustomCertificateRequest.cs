namespace Maran.Modules.Ssl.Controllers.Requests;

/// <summary>Body of <c>POST /api/v1/certificates/custom</c>: install material the customer supplied.</summary>
/// <remarks>
/// Written out by hand with an overridden <see cref="ToString"/> for the same reason the command is:
/// a positional record prints every property, and one of these properties is a private key. A request
/// object is exactly the thing a request-logging middleware or a model-binding diagnostic reaches for.
/// </remarks>
/// <param name="Domain">The domain to install for. It must be a site the caller owns.</param>
/// <param name="CertificatePem">PEM-encoded leaf certificate, optionally followed by its chain.</param>
/// <param name="PrivateKeyPem">PEM-encoded private key matching the certificate. Never logged, never stored.</param>
public sealed record InstallCustomCertificateRequest(
    string? Domain,
    string? CertificatePem,
    string? PrivateKeyPem)
{
    /// <summary>Describes the request without revealing the material it carries.</summary>
    /// <returns>A sentence naming the operation and the domain, and no material at all.</returns>
    public override string ToString()
    {
        return $"InstallCustomCertificateRequest({Domain})";
    }
}
