using FluentValidation;

namespace Maran.Modules.Identity.Commands.SwitchActiveAccount;

/// <summary>Bounds the fields of <see cref="SwitchActiveAccountCommand"/>.</summary>
public sealed class SwitchActiveAccountCommandValidator : AbstractValidator<SwitchActiveAccountCommand>
{
    /// <summary>Configures the field rules for <see cref="SwitchActiveAccountCommand"/>.</summary>
    public SwitchActiveAccountCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.AccountMembershipNotFound));
    }
}
