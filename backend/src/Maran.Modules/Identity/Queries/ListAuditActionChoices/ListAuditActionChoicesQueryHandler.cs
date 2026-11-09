using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Queries.ListAuditActionChoices;

/// <summary>
/// Handles <see cref="ListAuditActionChoicesQuery"/> by reading the distinct actions of
/// <c>identity.AuditEvents</c> and naming each for the request's culture.
/// </summary>
/// <remarks>
/// The answer is as long as the vocabulary of actions, not as long as the journal: the database
/// returns one row per distinct action, so the list stays the size of what the panel can record
/// however many entries it has written. It is ordered by the name the operator reads, which only
/// exists after the resource lookup, so the ordering happens here and not in SQL.
/// </remarks>
public sealed class ListAuditActionChoicesQueryHandler
{
    /// <summary>Narrows the journal to what the caller may read.</summary>
    private readonly AuditEventQuery _events;

    /// <summary>Decides which entries the caller may read.</summary>
    private readonly AuditEventScopeReader _scopeReader;

    /// <summary>Names each action as an operator reads it, in the request's culture.</summary>
    private readonly AuditActionDisplayNames _actionNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="events">Narrows the journal to what the caller may read.</param>
    /// <param name="scopeReader">Decides which entries the caller may read.</param>
    /// <param name="actionNames">Names an audit action for the request's culture.</param>
    public ListAuditActionChoicesQueryHandler(
        AuditEventQuery events,
        AuditEventScopeReader scopeReader,
        AuditActionDisplayNames actionNames)
    {
        _events = events;
        _scopeReader = scopeReader;
        _actionNames = actionNames;
    }

    /// <summary>
    /// Returns every action the caller's part of the journal holds, named and ordered by name: all of
    /// them for an administrator, those of its own accounts and login for a reseller.
    /// </summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The choices; <c>AuditJournalNotReadable</c> for a caller that is neither an administrator nor a reseller.
    /// </returns>
    public async Task<Result<IReadOnlyList<AuditActionChoiceDto>>> HandleAsync(
        ListAuditActionChoicesQuery query,
        CancellationToken cancellationToken)
    {
        var scope = await _scopeReader.ReadAsync(cancellationToken);
        if (!scope.IsSuccess)
        {
            return Result<IReadOnlyList<AuditActionChoiceDto>>.Fail(scope.Error!);
        }

        var actions = await _events.Visible(scope.Value)
            .Select(auditEvent => auditEvent.Action)
            .Distinct()
            .ToListAsync(cancellationToken);

        IReadOnlyList<AuditActionChoiceDto> choices = actions
            .Select(action =>
            {
                return new AuditActionChoiceDto(action, _actionNames.Of(action));
            })
            .OrderBy(
                choice =>
                {
                    return choice.ActionName;
                },
                StringComparer.CurrentCulture)
            .ThenBy(
                choice =>
                {
                    return choice.Action;
                },
                StringComparer.Ordinal)
            .ToList();

        return Result<IReadOnlyList<AuditActionChoiceDto>>.Ok(choices);
    }
}
