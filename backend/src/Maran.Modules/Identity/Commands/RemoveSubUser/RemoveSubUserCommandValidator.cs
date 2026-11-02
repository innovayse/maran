using FluentValidation;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Commands.RemoveSubUser;

/// <summary>Bounds the fields of <see cref="RemoveSubUserCommand"/>.</summary>
public sealed class RemoveSubUserCommandValidator : AbstractValidator<RemoveSubUserCommand>
{
    /// <summary>Configures the field rules for <see cref="RemoveSubUserCommand"/>.</summary>
    public RemoveSubUserCommandValidator()
    {
        RuleFor(command => command.UserId)
            .NotEmpty().WithMessage(nameof(ErrorMessages.SubUserNotFound));
    }
}
