using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>Handles <see cref="ListAuditEventsQuery"/> by reading <c>identity.AuditEvents</c>.</summary>
/// <remarks>
/// The rows are materialized first and projected after, because the projection names each row's
/// action for the request's culture through <see cref="AuditActionDisplayNames"/> — a resource
/// lookup no SQL translation can perform. The machine action stays in the DTO beside the name: the
/// screen shows the name, and the constant is what an administrator greps a log by.
/// </remarks>
public sealed class ListAuditEventsQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Names each row's action as an operator reads it, in the request's culture.</summary>
    private readonly AuditActionDisplayNames _actionNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="actionNames">Names an audit action for the request's culture.</param>
    public ListAuditEventsQueryHandler(IdentityDbContext dbContext, AuditActionDisplayNames actionNames)
    {
        _dbContext = dbContext;
        _actionNames = actionNames;
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
            .ToListAsync(cancellationToken);

        IReadOnlyList<AuditEventDto> projected = events
            .Select(e =>
            {
                return new AuditEventDto(
                    e.Id,
                    e.OccurredAt,
                    e.ActorUsername,
                    e.Action,
                    _actionNames.Of(e.Action),
                    e.Subject,
                    e.IpAddress,
                    e.Succeeded);
            })
            .ToList();

        return Result<IReadOnlyList<AuditEventDto>>.Ok(projected);
    }
}
