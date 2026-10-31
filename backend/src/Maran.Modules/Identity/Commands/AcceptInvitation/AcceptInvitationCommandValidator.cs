using FluentValidation;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Validators;

namespace Maran.Modules.Identity.Commands.AcceptInvitation;

/// <summary>
/// Bounds the fields of <see cref="AcceptInvitationCommand"/>, reading the password's minimum
/// length from the same operator-configurable security policy <c>ResetPasswordCommandValidator</c>
/// reads — there is exactly one password policy in this panel, and an invitation is a first
/// password, not a second one requiring a separate rule.
/// </summary>
/// <remarks>
/// The token is bounded but not otherwise checked, for the identical reason
/// <c>ResetPasswordCommandValidator</c> leaves its token unchecked: whether it is real is the
/// handler's question, and a validator that answered it here would answer with a different status
/// than the handler's own refusal — precisely the distinction a caller must not be able to draw
/// between a token that never existed and one that has expired or been spent.
/// </remarks>
public sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    /// <summary>Longest token the panel will even hash, bounding the work an anonymous caller can ask for.</summary>
    private const int MaxTokenLength = 128;

    /// <summary>Configures the field rules for <see cref="AcceptInvitationCommand"/>.</summary>
    /// <param name="policyCache">The panel's security policy, read for the minimum password length.</param>
    public AcceptInvitationCommandValidator(SecurityPolicyCache policyCache)
    {
        RuleFor(command => command.Token)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.InvitationTokenInvalid))
            .MaximumLength(MaxTokenLength).WithMessage(nameof(Resources.ErrorMessages.InvitationTokenInvalid));

        RuleFor(command => command.NewPassword).SetValidator(new NewPasswordValidator(policyCache));
    }
}
