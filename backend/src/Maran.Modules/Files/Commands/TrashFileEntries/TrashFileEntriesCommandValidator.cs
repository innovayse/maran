using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.TrashFileEntries;

/// <summary>Holds <see cref="TrashFileEntriesCommand"/> to a non-empty list of paths the panel accepts.</summary>
public sealed class TrashFileEntriesCommandValidator : AbstractValidator<TrashFileEntriesCommand>
{
    /// <summary>Configures the field rules for <see cref="TrashFileEntriesCommand"/>.</summary>
    public TrashFileEntriesCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Paths)
            .NotEmpty()
            .WithMessage(nameof(ErrorMessages.FilesBatchEmpty))
            .Must(FilesPathListRule.IsEveryEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid))
            .Must(FilesPathListRule.IsNoneReserved)
            .WithMessage(nameof(ErrorMessages.FilesPathReserved));
    }
}
