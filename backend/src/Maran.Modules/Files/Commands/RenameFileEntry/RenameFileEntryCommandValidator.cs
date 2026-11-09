using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.RenameFileEntry;

/// <summary>Holds <see cref="RenameFileEntryCommand"/> to a path and a single-segment name the panel accepts.</summary>
/// <remarks>
/// The new name is one segment with no separator, which is what keeps a rename from being a move to
/// another folder by another name.
/// </remarks>
public sealed class RenameFileEntryCommandValidator : AbstractValidator<RenameFileEntryCommand>
{
    /// <summary>Configures the field rules for <see cref="RenameFileEntryCommand"/>.</summary>
    public RenameFileEntryCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Path)
            .Must(FilesPathRule.IsEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid))
            .Must(path => { return !FilesPathRule.IsReserved(path); })
            .WithMessage(nameof(ErrorMessages.FilesPathReserved));

        RuleFor(command => command.NewName)
            .Must(FilesFileNameRule.IsValid)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid));
    }
}
