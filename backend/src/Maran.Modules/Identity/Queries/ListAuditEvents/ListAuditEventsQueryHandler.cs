using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>Handles <see cref="ListAuditEventsQuery"/> by reading <c>identity.AuditEvents</c>.</summary>
public sealed class ListAuditEventsQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public ListAuditEventsQueryHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the most recent entries, newest first.</summary>
    /// <param name="query">The bounded list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the entries; reading the journal never fails.</returns>
    public async Task<Result<IReadOnlyList<AuditEventDto>>> HandleAsync(
        ListAuditEventsQuery query,
        CancellationToken cancellationToken)
    {
        var events = await _dbContext.AuditEvents
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Take(query.Limit)
            .Select(e => new AuditEventDto(e.Id, e.OccurredAt, e.ActorUsername, e.Action, e.Subject, e.IpAddress, e.Succeeded))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AuditEventDto>>.Ok(events);
    }
}
