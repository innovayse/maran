using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.CreateFileEntry;

/// <summary>Holds <see cref="CreateFileEntryCommand"/> to a path the panel accepts (rules/security.md "Input").</summary>
/// <remarks>
/// The path rule is the panel being a good citizen, not the control: containment is the agent's
/// descriptor walk, and a path that passes here is checked again there. The home itself is not a new entry.
/// </remarks>
public sealed class CreateFileEntryCommandValidator : AbstractValidator<CreateFileEntryCommand>
{
    /// <summary>Configures the field rules for <see cref="CreateFileEntryCommand"/>.</summary>
    public CreateFileEntryCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Path)
            .Must(FilesPathRule.IsEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid))
            .Must(path => { return !FilesPathRule.IsReserved(path); })
            .WithMessage(nameof(ErrorMessages.FilesPathReserved));

        RuleFor(command => command.Kind)
            .IsInEnum();
    }
}
