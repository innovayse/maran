using FluentValidation;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Commands.SetCronEnvironment;

/// <summary>
/// Validates <see cref="SetCronEnvironmentCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// Two rules belong to the SET rather than to any one assignment, and neither can live in
/// <see cref="CronEnvironmentVariableValidator"/>:
///
/// A count ceiling, because the crontab preamble is written into a root-installed file and an
/// unbounded list is an unbounded file. It is set far above any real preamble.
///
/// And name uniqueness, because two assignments to one name are two crontab lines of which cron
/// applies the last — so the panel would show a value the host does not use, and a customer would
/// change the wrong one of the two. Refused rather than silently deduplicated: which of the two the
/// customer meant is not something this layer can know.
/// </remarks>
public sealed class SetCronEnvironmentCommandValidator : AbstractValidator<SetCronEnvironmentCommand>
{
    /// <summary>The most assignments the managed region of one crontab may hold.</summary>
    /// <remarks>
    /// Far above a real preamble — a handful of variables is the normal case — and low enough that
    /// the whole set stays something a person can read in the panel and an operator can read in the
    /// crontab.
    /// </remarks>
    private const int MaximumVariables = 32;

    /// <summary>Configures the field rules for <see cref="SetCronEnvironmentCommand"/>.</summary>
    public SetCronEnvironmentCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        // NotNull rather than NotEmpty: an EMPTY list is the documented way to clear every managed
        // assignment, so refusing it would remove the only way back from a preamble a customer no
        // longer wants.
        RuleFor(command => command.Variables)
            .NotNull()
            .WithMessage(nameof(ErrorMessages.CronEnvironmentInvalid));

        RuleFor(command => command.Variables)
            .Must(variables =>
            {
                return variables.Count <= MaximumVariables;
            })
            .WithMessage(nameof(ErrorMessages.CronEnvironmentTooManyVariables))
            .When(command =>
            {
                return command.Variables is not null;
            });

        RuleFor(command => command.Variables)
            .Must(variables =>
            {
                return variables.Select(variable =>
                {
                    return variable.Name;
                }).Distinct(StringComparer.Ordinal).Count() == variables.Count;
            })
            .WithMessage(nameof(ErrorMessages.CronEnvironmentDuplicateName))
            .When(command =>
            {
                return command.Variables is not null;
            });

        RuleForEach(command => command.Variables)
            .SetValidator(new CronEnvironmentVariableValidator())
            .When(command =>
            {
                return command.Variables is not null;
            });
    }
}
