using FluentValidation;
using Maran.Modules.Mail.Domain.Policies;

namespace Maran.Modules.Mail.Commands.UpdateMailAlias;

/// <summary>
/// Validates <see cref="UpdateMailAliasCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The agent re-validates every value, so this never substitutes for its boundary.
/// </summary>
public sealed class UpdateMailAliasCommandValidator : AbstractValidator<UpdateMailAliasCommand>
{
    /// <summary>Configures the field rules for <see cref="UpdateMailAliasCommand"/>.</summary>
    public UpdateMailAliasCommandValidator()
    {
        RuleFor(command => command.Targets)
            .Custom((targets, context) =>
            {
                var normalised = MailAliasTargetPolicy.Normalise(targets);
                if (!normalised.IsSuccess)
                {
                    context.AddFailure(normalised.Error!.Code);
                }
            });
    }
}
