using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>
/// Handles <see cref="ListAuditEventsQuery"/> by reading one page of <c>identity.AuditEvents</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every filter, the search, the count and the page run in PostgreSQL; only the page's rows are
/// materialized. They are projected after that, because the projection names each row's action for
/// the request's culture through <see cref="AuditActionDisplayNames"/> — a resource lookup no SQL
/// translation can perform. The machine action stays in the DTO beside the name.
/// </para>
/// <para>
/// <b>The search reads three columns and matches each literally.</b> The actor's name is how an
/// operator asks "what did this user do", the subject "what happened to this site", and the address
/// "what came from this host". <see cref="LikePattern"/> escapes the wildcards, so a login with an
/// underscore in it matches itself and not every name one character longer.
/// </para>
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

    /// <summary>
    /// Filters by action, outcome and window, searches the three text columns, and answers the
    /// requested page with the total that matched.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; reading the journal never fails.</returns>
    public async Task<Result<PagedResult<AuditEventDto>>> HandleAsync(
        ListAuditEventsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<AuditEvent> events = _dbContext.AuditEvents.AsNoTracking();

        if (!query.Action.IsNone)
        {
            var action = query.Action.Value;
            events = events.Where(auditEvent => auditEvent.Action == action);
        }

        if (query.Outcome is { } outcome)
        {
            var succeeded = outcome == AuditOutcome.Succeeded;
            events = events.Where(auditEvent => auditEvent.Succeeded == succeeded);
        }

        if (query.From is { } from)
        {
            events = events.Where(auditEvent => auditEvent.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            events = events.Where(auditEvent => auditEvent.OccurredAt <= to);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            events = events.Where(auditEvent =>
                EF.Functions.ILike(auditEvent.ActorUsername, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(auditEvent.Subject, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(auditEvent.IpAddress, pattern, LikePattern.EscapeCharacter));
        }

        var page = await events.ToPagedAsync(query, ListAuditEventsSorts.Map, cancellationToken);

        return Result<PagedResult<AuditEventDto>>.Ok(page.Map(auditEvent =>
        {
            return AuditEventMapper.From(auditEvent, _actionNames.Of(auditEvent.Action));
        }));
    }
}
