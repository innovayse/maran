using FluentValidation;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Validators;

namespace Maran.Modules.Identity.Commands.ChangePassword;

/// <summary>
/// Bounds the fields of <see cref="ChangePasswordCommand"/>: the new password through the panel's one
/// password rule (<see cref="NewPasswordValidator"/>), the current one only by length.
/// </summary>
/// <remarks>
/// Whether the current password is RIGHT is the handler's question, not this validator's: it is a
/// question about a stored hash, and answering it here would split one refusal across two places.
/// The ceiling still applies to it, because a megabyte-long "current password" would otherwise be
/// handed to Argon2id before anything refused it. Its failure carries the handler's code, so an
/// over-long guess and a wrong one are the same answer.
/// </remarks>
public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    /// <summary>Configures the field rules for <see cref="ChangePasswordCommand"/>.</summary>
    /// <param name="policyCache">The panel's security policy, read for the minimum password length.</param>
    public ChangePasswordCommandValidator(SecurityPolicyCache policyCache)
    {
        RuleFor(command => command.CurrentPassword)
            .NotEmpty().WithMessage(nameof(ErrorMessages.CurrentPasswordIncorrect))
            .MaximumLength(NewPasswordValidator.MaxPasswordLength)
            .WithMessage(nameof(ErrorMessages.CurrentPasswordIncorrect));

        RuleFor(command => command.NewPassword).SetValidator(new NewPasswordValidator(policyCache));
    }
}
