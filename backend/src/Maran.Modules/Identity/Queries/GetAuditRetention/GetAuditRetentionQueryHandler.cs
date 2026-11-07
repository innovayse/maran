using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Queries.GetAuditRetention;

/// <summary>Handles <see cref="GetAuditRetentionQuery"/> by reading the singleton retention row.</summary>
/// <remarks>
/// A panel that has never saved the setting has no row, and the absence IS the default
/// (<see cref="AuditRetentionPolicy.DefaultRetentionDays"/>, which keeps the journal indefinitely),
/// exactly as the security policy treats its own missing row: no seeder states the default a second time.
/// </remarks>
public sealed class GetAuditRetentionQueryHandler
{
    /// <summary>The module's database context, which owns the retention row.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public GetAuditRetentionQueryHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Reads the retention setting in force.</summary>
    /// <param name="query">The query; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The window, the bounds the panel accepts, and when the daily run last finished.</returns>
    public async Task<Result<AuditRetentionDto>> HandleAsync(
        GetAuditRetentionQuery query,
        CancellationToken cancellationToken)
    {
        var policy = await _dbContext.AuditRetentionPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == AuditRetentionPolicy.SingletonId, cancellationToken);

        return Result<AuditRetentionDto>.Ok(new AuditRetentionDto(
            policy is null ? AuditRetentionPolicy.DefaultRetentionDays : policy.RetentionDays,
            AuditRetentionPolicy.MinimumDays,
            AuditRetentionPolicy.MaximumDays,
            policy?.LastRunAt));
    }
}
