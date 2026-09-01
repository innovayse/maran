using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Persistence;

namespace Maran.Modules.Ssl.Queries.ListCertificates;

/// <summary>
/// Handles <see cref="ListCertificatesQuery"/>: the caller's certificates, soonest expiry first.
/// </summary>
/// <remarks>
/// There is no <c>Where</c> clause here and there must not be one. The rows are scoped by
/// <c>SslDbContext</c>'s global query filter, so a handler that forgot a filter still cannot leak —
/// which is the property the filter exists to provide (spec §8). Adding a redundant predicate here
/// would make it look as though the safety came from this line, and the next handler written by
/// copying this one would omit it and be wrong.
///
/// Ordered by expiry rather than by creation, because the question a customer opens this screen with
/// is "what is about to break".
/// </remarks>
public sealed class ListCertificatesQueryHandler
{
    /// <summary>The Ssl module's database context, carrying the caller's tenant scope.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    public ListCertificatesQueryHandler(SslDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Lists the certificates the caller may see.</summary>
    /// <param name="query">The query; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The caller's certificates, soonest expiry first.</returns>
    public async Task<Result<IReadOnlyList<CertificateDto>>> HandleAsync(
        ListCertificatesQuery query,
        CancellationToken cancellationToken)
    {
        var certificates = await _dbContext.Certificates
            .AsNoTracking()
            .OrderBy(certificate => certificate.NotAfter)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<CertificateDto>>.Ok(
            certificates.Select(CertificateDtoFactory.From).ToList());
    }
}
