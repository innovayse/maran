using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Queries.ListAuditOutcomeChoices;

/// <summary>
/// Handles <see cref="ListAuditOutcomeChoicesQuery"/> by naming every <see cref="AuditOutcome"/>
/// member for the request's culture.
/// </summary>
public sealed class ListAuditOutcomeChoicesQueryHandler
{
    /// <summary>Names each outcome as an administrator reads it, in the request's culture.</summary>
    private readonly AuditOutcomeDisplayNames _outcomeNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="outcomeNames">Names an audit outcome for the request's culture.</param>
    public ListAuditOutcomeChoicesQueryHandler(AuditOutcomeDisplayNames outcomeNames)
    {
        _outcomeNames = outcomeNames;
    }

    /// <summary>Returns every outcome, named, in declaration order.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the choices; this operation never fails.</returns>
    public Task<Result<IReadOnlyList<AuditOutcomeChoiceDto>>> HandleAsync(
        ListAuditOutcomeChoicesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AuditOutcomeChoiceDto> choices = Enum.GetValues<AuditOutcome>()
            .Select(outcome =>
            {
                return new AuditOutcomeChoiceDto(outcome, _outcomeNames.Of(outcome));
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<AuditOutcomeChoiceDto>>.Ok(choices));
    }
}
