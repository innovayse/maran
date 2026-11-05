using FluentValidation;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Monitoring.Queries.ListPendingUpdates;

/// <summary>
/// Holds <see cref="ListPendingUpdatesQuery"/> to the panel's paging bounds (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The search is held to its bounds by its binder. The classification filter is a closed vocabulary of
/// three words, so a value outside it names no row and is refused rather than answered with an empty
/// page the caller could mistake for "nothing is pending". The message is a bare resx key: only a key
/// reaches the caller, localized by the panel.
/// </remarks>
public sealed class ListPendingUpdatesQueryValidator
    : PagedQueryValidator<ListPendingUpdatesQuery, AgentPendingUpdate>
{
    /// <summary>Declares the paging rules against <see cref="ListPendingUpdatesSorts.Map"/>.</summary>
    public ListPendingUpdatesQueryValidator()
        : base(ListPendingUpdatesSorts.Map)
    {
        // None means "every update" and is the type's own state, so only a value is checked.
        RuleFor(query => query.Classification.Value)
            .Must(value =>
            {
                return PendingUpdateClassificationPolicy.All.Contains(value);
            })
            .WithErrorCode(nameof(ErrorMessages.PendingUpdateClassificationUnknown))
            .WithMessage(nameof(ErrorMessages.PendingUpdateClassificationUnknown))
            .OverridePropertyName(nameof(ListPendingUpdatesQuery.Classification))
            .When(query =>
            {
                return !query.Classification.IsNone;
            });
    }
}
