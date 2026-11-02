using FluentValidation;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Commands.RunCronEntry;

/// <summary>
/// Validates <see cref="RunCronEntryCommand"/> before it reaches the handler (rules/security.md "Input").
/// </summary>
public sealed class RunCronEntryCommandValidator : AbstractValidator<RunCronEntryCommand>
{
    /// <summary>Configures the field rules for <see cref="RunCronEntryCommand"/>.</summary>
    public RunCronEntryCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.EntryId)
            .Matches(CronEntryIdRule.Pattern)
            .WithMessage(nameof(ErrorMessages.CronEntryIdInvalid));
    }
}
