using FluentValidation;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Commands.DeleteCronEntry;

/// <summary>
/// Validates <see cref="DeleteCronEntryCommand"/> before it reaches the handler (rules/security.md
/// "Input").
/// </summary>
/// <remarks>
/// The entry-id rule is the shared one. It matters most on this operation and on the output read:
/// the agent turns an id into three file paths under the account's home, so an id shaped like a path
/// is the thing both layers exist to refuse.
/// </remarks>
public sealed class DeleteCronEntryCommandValidator : AbstractValidator<DeleteCronEntryCommand>
{
    /// <summary>Configures the field rules for <see cref="DeleteCronEntryCommand"/>.</summary>
    public DeleteCronEntryCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.EntryId)
            .Matches(CronEntryIdRule.Pattern)
            .WithMessage(nameof(ErrorMessages.CronEntryIdInvalid));
    }
}
