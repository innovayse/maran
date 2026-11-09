using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.SharedKernel.Extensions;

namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>
/// Handles <see cref="ListAuditEventsQuery"/> by reading one page of <c>identity.AuditEvents</c>.
/// </summary>
/// <remarks>
/// Every filter, the search, the count and the page run in PostgreSQL; only the page's rows are
/// materialized. The filters themselves are <see cref="AuditEventQuery"/>'s, shared with every other
/// reader of the journal. The rows are projected after the page is cut, because the projection names
/// each row's action for the request's culture through <see cref="AuditActionDisplayNames"/> — a
/// resource lookup no SQL translation can perform. The machine action stays in the DTO beside the name.
/// What the caller may read is <see cref="AuditEventScopeReader"/>'s decision, made before any filter
/// runs: an administrator reads the whole journal and a reseller only its own accounts' entries and its
/// own login's (issue #370), so one query, one validator and one page shape serve both screens.
/// </remarks>
public sealed class ListAuditEventsQueryHandler
{
    /// <summary>Narrows the journal by the request's filters.</summary>
    private readonly AuditEventQuery _events;

    /// <summary>Decides which entries the caller may read.</summary>
    private readonly AuditEventScopeReader _scopeReader;

    /// <summary>Names each row's action as an operator reads it, in the request's culture.</summary>
    private readonly AuditActionDisplayNames _actionNames;

    /// <summary>Names an audit outcome as an administrator reads it, in the request's culture.</summary>
    private readonly AuditOutcomeDisplayNames _outcomeNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="events">Narrows the journal by the request's filters.</param>
    /// <param name="scopeReader">Decides which entries the caller may read.</param>
    /// <param name="actionNames">Names an audit action for the request's culture.</param>
    /// <param name="outcomeNames">Names an audit outcome for the request's culture.</param>
    public ListAuditEventsQueryHandler(
        AuditEventQuery events,
        AuditEventScopeReader scopeReader,
        AuditActionDisplayNames actionNames,
        AuditOutcomeDisplayNames outcomeNames)
    {
        _events = events;
        _scopeReader = scopeReader;
        _actionNames = actionNames;
        _outcomeNames = outcomeNames;
    }

    /// <summary>
    /// Filters by action, outcome, window, actor and account, searches the text columns, and answers
    /// the requested page with the total that matched.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// The page; <c>AuditJournalNotReadable</c> for a caller that is neither an administrator nor a reseller;
    /// <c>AuditAccountNotFound</c> when a reseller filters by an account it does not own.
    /// </returns>
    public async Task<Result<PagedResult<AuditEventDto>>> HandleAsync(
        ListAuditEventsQuery query,
        CancellationToken cancellationToken)
    {
        var scope = await _scopeReader.ReadAsync(cancellationToken);
        if (!scope.IsSuccess)
        {
            return Result<PagedResult<AuditEventDto>>.Fail(scope.Error!);
        }

        // An account outside a reseller's scope answers exactly as one that does not exist, so the
        // filter cannot be used to find out which accounts the panel holds (rules/security.md item 6).
        if (scope.Value is ResellerEntriesScope reseller && query.Account is { } account && !reseller.Owns(account))
        {
            return Result<PagedResult<AuditEventDto>>.Fail(
                Error.Of(nameof(ErrorMessages.AuditAccountNotFound), ErrorType.NotFound));
        }

        var events = _events.Matching(new AuditEventFilter(
            query.Action,
            query.Outcome,
            query.From,
            query.To,
            query.Search,
            query.Actor,
            query.Account), scope.Value);

        var page = await events.ToPagedAsync(query, ListAuditEventsSorts.Map, cancellationToken);

        return Result<PagedResult<AuditEventDto>>.Ok(page.Map(auditEvent =>
        {
            return AuditEventMapper.From(auditEvent, _actionNames.Of(auditEvent.Action), _outcomeNames.Of(auditEvent.Succeeded));
        }));
    }
}
