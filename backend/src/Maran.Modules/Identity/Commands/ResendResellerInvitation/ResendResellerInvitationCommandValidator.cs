using FluentValidation;

namespace Maran.Modules.Identity.Commands.ResendResellerInvitation;

/// <summary>Bounds the fields of <see cref="ResendResellerInvitationCommand"/>.</summary>
public sealed class ResendResellerInvitationCommandValidator : AbstractValidator<ResendResellerInvitationCommand>
{
    /// <summary>Configures the field rules for <see cref="ResendResellerInvitationCommand"/>.</summary>
    public ResendResellerInvitationCommandValidator()
    {
        RuleFor(command => command.ResellerId)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.InvitationResellerNotFound));
    }
}
