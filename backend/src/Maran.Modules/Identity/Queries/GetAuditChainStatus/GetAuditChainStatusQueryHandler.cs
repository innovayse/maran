using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Queries.GetAuditChainStatus;

/// <summary>Handles <see cref="GetAuditChainStatusQuery"/> by counting the journal's sealed and unsealed entries.</summary>
/// <remarks>
/// The counts are the administrator's only view of the unsealed window: entries written since the last
/// sealing run are not yet protected, and a large number after an upgrade is the backlog being sealed in
/// batches, not a fault.
/// </remarks>
public sealed class GetAuditChainStatusQueryHandler
{
    /// <summary>The module's database context, which owns the journal and the chain's anchor.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public GetAuditChainStatusQueryHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Reads the chain's status.</summary>
    /// <param name="query">The query; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The counts, the newest sealed position and the purge point.</returns>
    public async Task<Result<AuditChainStatusDto>> HandleAsync(
        GetAuditChainStatusQuery query,
        CancellationToken cancellationToken)
    {
        var journal = _dbContext.AuditEvents.AsNoTracking();

        var sealedRows = await journal.LongCountAsync(row => row.ChainIndex != null, cancellationToken);
        var unsealedRows = await journal.LongCountAsync(row => row.ChainIndex == null, cancellationToken);

        var newest = await journal
            .Where(row => row.ChainIndex != null)
            .OrderByDescending(row => row.ChainIndex)
            .Select(row => new { row.ChainIndex, row.OccurredAt })
            .FirstOrDefaultAsync(cancellationToken);

        var anchor = await _dbContext.AuditChainAnchors
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return Result<AuditChainStatusDto>.Ok(new AuditChainStatusDto(
            sealedRows,
            unsealedRows,
            newest?.ChainIndex,
            newest?.OccurredAt,
            anchor?.PurgedThroughIndex ?? 0));
    }
}
