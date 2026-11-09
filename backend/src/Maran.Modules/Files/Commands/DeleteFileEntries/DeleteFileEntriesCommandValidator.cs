using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.DeleteFileEntries;

/// <summary>Holds <see cref="DeleteFileEntriesCommand"/> to a non-empty list of paths and an explicit confirmation.</summary>
/// <remarks>A permanent delete cannot be undone, so a request that did not say it was confirmed is refused.</remarks>
public sealed class DeleteFileEntriesCommandValidator : AbstractValidator<DeleteFileEntriesCommand>
{
    /// <summary>Configures the field rules for <see cref="DeleteFileEntriesCommand"/>.</summary>
    public DeleteFileEntriesCommandValidator()
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

        RuleFor(command => command.Confirm)
            .Equal(true)
            .WithMessage(nameof(ErrorMessages.FilesDeleteNotConfirmed));
    }
}
