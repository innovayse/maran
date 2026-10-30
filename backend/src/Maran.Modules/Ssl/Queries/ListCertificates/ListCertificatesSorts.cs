using Maran.Modules.Ssl.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Ssl.Queries.ListCertificates;

/// <summary>
/// The orders the certificates list accepts. <see cref="ListCertificatesQueryValidator"/> refuses any
/// other sort and <see cref="ListCertificatesQueryHandler"/> orders by exactly these, so the whitelist
/// is written once.
/// </summary>
public static class ListCertificatesSorts
{
    /// <summary>
    /// <see cref="CertificateSortFields.NotAfter"/> and <see cref="CertificateSortFields.Domain"/>,
    /// soonest expiry first when none is named — the question a customer opens the list with is "what
    /// is about to break", and it is the order the list had before it was paged — with ties broken by
    /// the certificate id.
    /// </summary>
    public static readonly SortMap<Certificate> Map = new SortMapBuilder<Certificate>()
        .By(CertificateSortFields.NotAfter, certificate => certificate.NotAfter)
        .By(CertificateSortFields.Domain, certificate => certificate.Domain)
        .DefaultTo(CertificateSortFields.NotAfter, SortDirection.Ascending)
        .TieBreaker(certificate => certificate.Id);
}
