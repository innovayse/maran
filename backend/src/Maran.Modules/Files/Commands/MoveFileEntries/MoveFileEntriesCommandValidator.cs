using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.MoveFileEntries;

/// <summary>Holds <see cref="MoveFileEntriesCommand"/> to a non-empty list of paths and a destination folder the panel accepts.</summary>
/// <remarks>
/// The cap on the number of items is the batch runner's, checked before the first agent call, because
/// the limit is configuration and a validator holds no options.
/// </remarks>
public sealed class MoveFileEntriesCommandValidator : AbstractValidator<MoveFileEntriesCommand>
{
    /// <summary>Configures the field rules for <see cref="MoveFileEntriesCommand"/>.</summary>
    public MoveFileEntriesCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.SourcePaths)
            .NotEmpty()
            .WithMessage(nameof(ErrorMessages.FilesBatchEmpty))
            .Must(FilesPathListRule.IsEveryEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid))
            .Must(FilesPathListRule.IsNoneReserved)
            .WithMessage(nameof(ErrorMessages.FilesPathReserved));

        RuleFor(command => command.DestinationDirectory)
            .Must(FilesPathRule.IsWellFormed)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid))
            .Must(path => { return !FilesPathRule.IsReserved(path); })
            .WithMessage(nameof(ErrorMessages.FilesPathReserved))
            .Must((command, path) => { return FilesPathListRule.IsNoneContaining(command.SourcePaths, path); })
            .WithMessage(nameof(ErrorMessages.FilesDestinationInsideSource));
    }
}
