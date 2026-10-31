using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Queries.ListRecentAuditEvents;

/// <summary>
/// Handles <see cref="ListRecentAuditEventsQuery"/> by reading the newest rows of
/// <c>identity.AuditEvents</c>, for the landing page's short excerpt of the journal.
/// </summary>
/// <remarks>
/// The rows are materialized first and projected after, because the projection names each row's
/// action for the request's culture through <see cref="AuditActionDisplayNames"/> — a resource
/// lookup no SQL translation can perform. The machine action stays in the DTO beside the name: the
/// screen shows the name, and the constant is what an administrator greps a log by.
/// </remarks>
public sealed class ListRecentAuditEventsQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Names each row's action as an operator reads it, in the request's culture.</summary>
    private readonly AuditActionDisplayNames _actionNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="actionNames">Names an audit action for the request's culture.</param>
    public ListRecentAuditEventsQueryHandler(IdentityDbContext dbContext, AuditActionDisplayNames actionNames)
    {
        _dbContext = dbContext;
        _actionNames = actionNames;
    }

    /// <summary>Returns the most recent entries, newest first.</summary>
    /// <param name="query">The bounded list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the entries; reading the journal never fails.</returns>
    public async Task<Result<IReadOnlyList<AuditEventDto>>> HandleAsync(
        ListRecentAuditEventsQuery query,
        CancellationToken cancellationToken)
    {
        var events = await _dbContext.AuditEvents
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        IReadOnlyList<AuditEventDto> projected = events
            .Select(auditEvent =>
            {
                return AuditEventMapper.From(auditEvent, _actionNames.Of(auditEvent.Action));
            })
            .ToList();

        return Result<IReadOnlyList<AuditEventDto>>.Ok(projected);
    }
}
