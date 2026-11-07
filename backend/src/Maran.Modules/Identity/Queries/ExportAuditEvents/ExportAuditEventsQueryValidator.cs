using FluentValidation;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Queries.ExportAuditEvents;

/// <summary>
/// Holds <see cref="ExportAuditEventsQuery"/> to its own bounds (rules/security.md "Input"): a known
/// format, filters the journal's columns could hold, and a window that is not inverted.
/// </summary>
/// <remarks>
/// <para>
/// Each message is a bare resx key: only a key reaches the caller, localized by the panel. The filter
/// rules restate the journal list's, with the same codes, because a filter that is refused in the list
/// must be refused in the export; the export has no paging rules because it has no paging.
/// </para>
/// <para>
/// <b>The row cap is not checked here.</b> Whether an export is too large depends on how many rows match,
/// which only the database knows, so <see cref="ExportAuditEventsQueryHandler"/> counts and refuses.
/// </para>
/// </remarks>
public sealed class ExportAuditEventsQueryValidator : AbstractValidator<ExportAuditEventsQuery>
{
    /// <summary>
    /// The characters an action or actor filter may carry: the alphabet of the machine-stable action
    /// names and of the login names the journal records. A value outside it names no row, so it is
    /// refused rather than answered with an empty file.
    /// </summary>
    private const string FilterPattern = "^[A-Za-z0-9._-]+$";

    /// <summary>Declares the format, the action, outcome, actor and window rules.</summary>
    public ExportAuditEventsQueryValidator()
    {
        // Over HTTP, ?format=xml never reaches this rule: MVC's binder refuses a name naming no member,
        // answered as RequestParameterInvalid. The rule holds the query to its members for a caller that
        // dispatches it on the bus without HTTP binding.
        RuleFor(query => query.Format)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.AuditExportFormatUnknown))
            .WithMessage(nameof(ErrorMessages.AuditExportFormatUnknown));

        // None means "every action" and is the type's own state, so only a value is checked.
        RuleFor(query => query.Action.Value)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(AuditEvent.ActionMaxLength)
            .WithErrorCode(nameof(ErrorMessages.AuditActionFilterInvalid))
            .WithMessage(nameof(ErrorMessages.AuditActionFilterInvalid))
            .Matches(FilterPattern)
            .WithErrorCode(nameof(ErrorMessages.AuditActionFilterInvalid))
            .WithMessage(nameof(ErrorMessages.AuditActionFilterInvalid))
            .OverridePropertyName(nameof(ExportAuditEventsQuery.Action))
            .When(query =>
            {
                return !query.Action.IsNone;
            });

        RuleFor(query => query.Actor.Value)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(AuditEvent.ActorUsernameMaxLength)
            .WithErrorCode(nameof(ErrorMessages.AuditActorFilterInvalid))
            .WithMessage(nameof(ErrorMessages.AuditActorFilterInvalid))
            .Matches(FilterPattern)
            .WithErrorCode(nameof(ErrorMessages.AuditActorFilterInvalid))
            .WithMessage(nameof(ErrorMessages.AuditActorFilterInvalid))
            .OverridePropertyName(nameof(ExportAuditEventsQuery.Actor))
            .When(query =>
            {
                return !query.Actor.IsNone;
            });

        RuleFor(query => query.Outcome)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.AuditOutcomeUnknown))
            .WithMessage(nameof(ErrorMessages.AuditOutcomeUnknown));

        // An inverted window matches nothing; refusing it says why the file would be empty instead of
        // handing over a header and no rows the caller cannot tell from "nothing happened then".
        RuleFor(query => query.From)
            .Must((query, from) =>
            {
                return from is null || query.To is null || from.Value <= query.To.Value;
            })
            .WithErrorCode(nameof(ErrorMessages.AuditDateRangeInverted))
            .WithMessage(nameof(ErrorMessages.AuditDateRangeInverted));
    }
}
