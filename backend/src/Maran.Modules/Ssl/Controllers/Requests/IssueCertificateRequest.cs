namespace Maran.Modules.Ssl.Controllers.Requests;

/// <summary>Body of <c>POST /api/v1/certificates</c>: order a certificate for one of my sites.</summary>
/// <param name="Domain">The domain to issue for. It must be a site the caller owns.</param>
public sealed record IssueCertificateRequest(string? Domain);
