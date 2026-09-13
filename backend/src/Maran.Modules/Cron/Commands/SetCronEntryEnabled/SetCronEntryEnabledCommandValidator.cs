using FluentValidation;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Commands.SetCronEntryEnabled;

/// <summary>
/// Validates <see cref="SetCronEntryEnabledCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// There is no rule on the flag itself: a boolean has two values and both are legal requests. The
/// entry id carries the shared rule, as on every operation that names one entry.
/// </remarks>
public sealed class SetCronEntryEnabledCommandValidator : AbstractValidator<SetCronEntryEnabledCommand>
{
    /// <summary>Configures the field rules for <see cref="SetCronEntryEnabledCommand"/>.</summary>
    public SetCronEntryEnabledCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.EntryId)
            .Matches(CronEntryIdRule.Pattern)
            .WithMessage(nameof(ErrorMessages.CronEntryIdInvalid));
    }
}
