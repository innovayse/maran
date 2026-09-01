using Maran.Modules.Ssl.Domain;

namespace Maran.Modules.Ssl.Common;

/// <summary>Projects a stored <see cref="Certificate"/> onto the shape the API returns.</summary>
/// <remarks>
/// One conversion, so no handler assembles a DTO by hand and no future field is accidentally added
/// to the response by one call site and not another. It is also the place to notice if anybody ever
/// tries to project material into a response: there is none in the entity to project.
/// </remarks>
public static class CertificateDtoFactory
{
    /// <summary>Describes a stored certificate to the caller.</summary>
    /// <param name="certificate">The row that records the certificate.</param>
    /// <returns>The DTO carrying that row's own facts.</returns>
    public static CertificateDto From(Certificate certificate)
    {
        return new CertificateDto(
            certificate.Id,
            certificate.SiteId,
            certificate.Domain,
            certificate.Source,
            certificate.NotAfter,
            certificate.IssuedAt,
            certificate.LastRenewalAttemptAt,
            certificate.LastRenewalErrorCode,
            certificate.ConsecutiveRenewalFailures);
    }
}
