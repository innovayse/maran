using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Jobs;
using Maran.Modules.Ssl.Mappers;
using Maran.Modules.Ssl.Persistence;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Ssl.Queries.ListCertificates;

/// <summary>Handles <see cref="ListCertificatesQuery"/> by reading one page of <c>ssl.Certificates</c>.</summary>
/// <remarks>
/// <para>
/// The tenant boundary is not any <c>Where</c> clause here, and none of the clauses below may be read
/// as one. The rows are scoped by <c>SslDbContext</c>'s global query filter, before the count as well
/// as before the page, so a customer's total can never count another tenant's rows (spec §8). Every
/// clause below only narrows that scope; a handler copied from this one that dropped them all would
/// still not leak.
/// </para>
/// <para>
/// The status and the expiring window are measured against one instant read once from
/// <see cref="IClock"/>, so a certificate expiring during the request cannot land in two states.
/// </para>
/// </remarks>
public sealed class ListCertificatesQueryHandler
{
    /// <summary>The Ssl module's database context, carrying the caller's tenant scope.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The clock the status and the expiring window are measured against.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    /// <param name="clock">The clock the status and the expiring window are measured against.</param>
    public ListCertificatesQueryHandler(SslDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>
    /// Filters by site, status and the expiring window, searches the domain, and answers the requested
    /// page with the total that matched.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<CertificateDto>>> HandleAsync(
        ListCertificatesQuery query,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        IQueryable<Certificate> certificates = _dbContext.Certificates.AsNoTracking();

        if (query.SiteId is { } siteId)
        {
            certificates = certificates.Where(certificate => certificate.SiteId == siteId);
        }

        // The three states exactly as CertificateStatus defines them: exclusive, and covering every row.
        certificates = query.Status switch
        {
            CertificateStatus.Valid => certificates.Where(certificate =>
                certificate.NotAfter > now && certificate.ConsecutiveRenewalFailures == 0),
            CertificateStatus.RenewalFailing => certificates.Where(certificate =>
                certificate.NotAfter > now && certificate.ConsecutiveRenewalFailures > 0),
            CertificateStatus.Expired => certificates.Where(certificate => certificate.NotAfter <= now),
            _ => certificates,
        };

        if (query.Expiring)
        {
            // The renewal job's own window, so "expiring" here is the span in which the panel re-orders.
            var horizon = now + CertificateRenewalHandler.RenewalWindow;
            certificates = certificates.Where(certificate => certificate.NotAfter > now && certificate.NotAfter <= horizon);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            certificates = certificates.Where(certificate =>
                EF.Functions.ILike(certificate.Domain, pattern, LikePattern.EscapeCharacter));
        }

        var page = await certificates.ToPagedAsync(query, ListCertificatesSorts.Map, cancellationToken);

        return Result<PagedResult<CertificateDto>>.Ok(page.Map(CertificateMapper.From));
    }
}
