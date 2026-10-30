using Maran.Modules.Ssl.Persistence;

namespace Maran.Modules.Ssl.Queries.CountExpiringCertificates;

/// <summary>
/// Handles <see cref="CountExpiringCertificatesQuery"/> with one <c>count(*)</c> over <c>ssl.Certificates</c>.
/// </summary>
/// <remarks>
/// Scoped by the context's global query filter like every other read of the table, so a customer's
/// count is of their own certificates and an administrator's is of every certificate on the server.
/// </remarks>
public sealed class CountExpiringCertificatesQueryHandler
{
    /// <summary>The Ssl module's database context, carrying the caller's tenant scope.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    public CountExpiringCertificatesQueryHandler(SslDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Counts the certificates in the caller's tenant scope expiring at or before the instant asked.</summary>
    /// <param name="query">The count request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the count; this operation never fails.</returns>
    public async Task<Result<int>> HandleAsync(CountExpiringCertificatesQuery query, CancellationToken cancellationToken)
    {
        var before = query.Before;
        var count = await _dbContext.Certificates
            .CountAsync(certificate => certificate.NotAfter <= before, cancellationToken);

        return Result<int>.Ok(count);
    }
}
