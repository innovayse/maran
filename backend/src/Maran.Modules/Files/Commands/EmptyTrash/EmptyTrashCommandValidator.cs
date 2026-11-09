using FluentValidation;

namespace Maran.Modules.Files.Commands.EmptyTrash;

/// <summary>Holds <see cref="EmptyTrashCommand"/> to an account id; the command carries nothing else to validate.</summary>
public sealed class EmptyTrashCommandValidator : AbstractValidator<EmptyTrashCommand>
{
    /// <summary>Configures the field rules for <see cref="EmptyTrashCommand"/>.</summary>
    public EmptyTrashCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();
    }
}
