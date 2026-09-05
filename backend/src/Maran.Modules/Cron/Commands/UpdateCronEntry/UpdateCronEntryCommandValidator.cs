using FluentValidation;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Commands.UpdateCronEntry;

/// <summary>
/// Validates <see cref="UpdateCronEntryCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Every one of these is re-validated inside the agent as well: the API's validation never
/// substitutes for the agent's own boundary check (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// The schedule, command and entry-id rules are the shared ones, so an update cannot come to accept
/// an entry a creation would refuse — or an identifier shape the agent narrowed for path safety.
/// </remarks>
public sealed class UpdateCronEntryCommandValidator : AbstractValidator<UpdateCronEntryCommand>
{
    /// <summary>Configures the field rules for <see cref="UpdateCronEntryCommand"/>.</summary>
    public UpdateCronEntryCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.EntryId)
            .Matches(CronEntryIdRule.Pattern)
            .WithMessage(nameof(ErrorMessages.CronEntryIdInvalid));

        RuleFor(command => command.Schedule)
            .NotNull()
            .WithMessage(nameof(ErrorMessages.CronScheduleInvalid));

        RuleFor(command => command.Schedule)
            .SetValidator(new CronScheduleValidator())
            .When(command =>
            {
                return command.Schedule is not null;
            });

        RuleFor(command => command.Command)
            .Must(command =>
            {
                return CronCommandRule.IsOneCommandLine(command);
            })
            .WithMessage(nameof(ErrorMessages.CronCommandInvalid));
    }
}
