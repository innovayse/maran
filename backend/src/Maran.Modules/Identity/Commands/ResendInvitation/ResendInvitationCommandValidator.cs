using FluentValidation;

namespace Maran.Modules.Identity.Commands.ResendInvitation;

/// <summary>Bounds the fields of <see cref="ResendInvitationCommand"/>.</summary>
public sealed class ResendInvitationCommandValidator : AbstractValidator<ResendInvitationCommand>
{
    /// <summary>Configures the field rules for <see cref="ResendInvitationCommand"/>.</summary>
    public ResendInvitationCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.InvitationAccountNotFound));
    }
}
