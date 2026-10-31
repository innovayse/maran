using FluentValidation;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Tasks.Queries.ListTasks;

/// <summary>
/// Holds <see cref="ListTasksQuery"/> to the panel's paging bounds and its own filters
/// (rules/security.md "Input").
/// </summary>
/// <remarks>Each message is a bare resx key: only a key reaches the caller, localized by the panel.</remarks>
public sealed class ListTasksQueryValidator : PagedQueryValidator<ListTasksQuery, PanelTask>
{
    /// <summary>
    /// The characters a kind filter may carry: the alphabet of the machine-stable kinds the panel
    /// records. A value outside it names no row, so it is refused rather than answered empty.
    /// </summary>
    private const string KindPattern = "^[A-Za-z0-9._-]+$";

    /// <summary>Declares the paging rules against <see cref="ListTasksSorts.Map"/>, then the filters.</summary>
    public ListTasksQueryValidator()
        : base(ListTasksSorts.Map)
    {
        // Over HTTP, ?status=7 never reaches this rule: MVC's binder refuses a number naming no
        // member, answered as RequestParameterInvalid. The rule holds the query to its members for a
        // caller that dispatches it on the bus without HTTP binding.
        RuleFor(query => query.Status)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.TaskStatusUnknown))
            .WithMessage(nameof(ErrorMessages.TaskStatusUnknown));

        // None means "every kind" and is the type's own state, so only a value is checked: it must be
        // something the column could hold.
        RuleFor(query => query.Kind.Value)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(PanelTask.KindMaxLength)
            .WithErrorCode(nameof(ErrorMessages.TaskKindFilterInvalid))
            .WithMessage(nameof(ErrorMessages.TaskKindFilterInvalid))
            .Matches(KindPattern)
            .WithErrorCode(nameof(ErrorMessages.TaskKindFilterInvalid))
            .WithMessage(nameof(ErrorMessages.TaskKindFilterInvalid))
            .OverridePropertyName(nameof(ListTasksQuery.Kind))
            .When(query =>
            {
                return !query.Kind.IsNone;
            });

        // An inverted window matches nothing; refusing it says why the page would be empty instead of
        // answering a silent zero the caller cannot tell from "nothing ran then".
        RuleFor(query => query.From)
            .Must((query, from) =>
            {
                return from is null || query.To is null || from.Value <= query.To.Value;
            })
            .WithErrorCode(nameof(ErrorMessages.TaskDateRangeInverted))
            .WithMessage(nameof(ErrorMessages.TaskDateRangeInverted));
    }
}
