using FluentValidation;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Commands.ResendSubUserInvitation;

/// <summary>Bounds the fields of <see cref="ResendSubUserInvitationCommand"/>.</summary>
public sealed class ResendSubUserInvitationCommandValidator : AbstractValidator<ResendSubUserInvitationCommand>
{
    /// <summary>Configures the field rules for <see cref="ResendSubUserInvitationCommand"/>.</summary>
    public ResendSubUserInvitationCommandValidator()
    {
        RuleFor(command => command.UserId)
            .NotEmpty().WithMessage(nameof(ErrorMessages.SubUserNotFound));
    }
}
