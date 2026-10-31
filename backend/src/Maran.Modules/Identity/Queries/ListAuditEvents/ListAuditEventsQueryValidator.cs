using FluentValidation;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>
/// Holds <see cref="ListAuditEventsQuery"/> to the panel's paging bounds and its own filters
/// (rules/security.md "Input").
/// </summary>
/// <remarks>Each message is a bare resx key: only a key reaches the caller, localized by the panel.</remarks>
public sealed class ListAuditEventsQueryValidator : PagedQueryValidator<ListAuditEventsQuery, AuditEvent>
{
    /// <summary>
    /// The characters an action filter may carry: the alphabet of the machine-stable action names the
    /// journal records. A value outside it names no row, so it is refused rather than answered empty.
    /// </summary>
    private const string ActionPattern = "^[A-Za-z0-9._-]+$";

    /// <summary>Declares the paging rules against <see cref="ListAuditEventsSorts.Map"/>, then the filters.</summary>
    public ListAuditEventsQueryValidator()
        : base(ListAuditEventsSorts.Map)
    {
        // None means "every action" and is the type's own state, so only a value is checked: it must be
        // something the column could hold.
        RuleFor(query => query.Action.Value)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(AuditEvent.ActionMaxLength)
            .WithErrorCode(nameof(ErrorMessages.AuditActionFilterInvalid))
            .WithMessage(nameof(ErrorMessages.AuditActionFilterInvalid))
            .Matches(ActionPattern)
            .WithErrorCode(nameof(ErrorMessages.AuditActionFilterInvalid))
            .WithMessage(nameof(ErrorMessages.AuditActionFilterInvalid))
            .OverridePropertyName(nameof(ListAuditEventsQuery.Action))
            .When(query =>
            {
                return !query.Action.IsNone;
            });

        // Over HTTP, ?outcome=7 never reaches this rule: MVC's binder refuses a number naming no
        // member, answered as RequestParameterInvalid. The rule holds the query to its members for a
        // caller that dispatches it on the bus without HTTP binding.
        RuleFor(query => query.Outcome)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.AuditOutcomeUnknown))
            .WithMessage(nameof(ErrorMessages.AuditOutcomeUnknown));

        // An inverted window matches nothing; refusing it says why the page would be empty instead of
        // answering a silent zero the caller cannot tell from "nothing happened then".
        RuleFor(query => query.From)
            .Must((query, from) =>
            {
                return from is null || query.To is null || from.Value <= query.To.Value;
            })
            .WithErrorCode(nameof(ErrorMessages.AuditDateRangeInverted))
            .WithMessage(nameof(ErrorMessages.AuditDateRangeInverted));
    }
}
