using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Domain.Entities;

namespace Maran.Modules.Ssl.Mappers;

/// <summary>Projects a stored <see cref="Certificate"/> onto the shape the API returns.</summary>
/// <remarks>
/// One conversion, so no handler assembles a DTO by hand and no future field is accidentally added
/// to the response by one call site and not another. It is also the place to notice if anybody ever
/// tries to project material into a response: there is none in the entity to project.
/// </remarks>
public static class CertificateMapper
{
    /// <summary>Describes a stored certificate to the caller.</summary>
    /// <param name="certificate">The row that records the certificate.</param>
    /// <param name="lastRenewalError">
    /// The sentence for the row's last renewal failure, resolved by the caller, or null when there is
    /// none. The mapper restates it and never works out which case it is in.
    /// </param>
    /// <returns>The DTO carrying that row's own facts.</returns>
    public static CertificateDto From(Certificate certificate, string? lastRenewalError)
    {
        return new CertificateDto(
            certificate.Id,
            certificate.SiteId,
            certificate.Domain,
            certificate.Source,
            certificate.NotAfter,
            certificate.IssuedAt,
            certificate.LastRenewalAttemptAt,
            certificate.LastRenewalFailureCode,
            lastRenewalError,
            certificate.ConsecutiveRenewalFailures,
            certificate.IsWildcard);
    }
}
