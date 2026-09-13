using FluentValidation;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Commands.CreateCronEntry;

/// <summary>
/// Validates <see cref="CreateCronEntryCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Every one of these is re-validated inside the agent as well: the API's validation never
/// substitutes for the agent's own boundary check (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// The schedule's rules live in <see cref="CronScheduleValidator"/> and the command's in
/// <see cref="CronCommandRule"/>, both shared with the update operation, so the two ways of writing
/// an entry cannot come to accept different entries.
///
/// Each message is a bare resx key, not an English sentence. The Host forwards a validation message
/// only when it is entirely alphanumeric, and then resolves it as an error code against the module's
/// resources; an English sentence is silently discarded and the customer gets the generic failure
/// instead.
/// </remarks>
public sealed class CreateCronEntryCommandValidator : AbstractValidator<CreateCronEntryCommand>
{
    /// <summary>Configures the field rules for <see cref="CreateCronEntryCommand"/>.</summary>
    public CreateCronEntryCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

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
