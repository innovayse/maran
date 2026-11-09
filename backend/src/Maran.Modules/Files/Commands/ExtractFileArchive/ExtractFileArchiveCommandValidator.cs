using FluentValidation;
using Maran.Modules.Files.Domain.Policies;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.ExtractFileArchive;

/// <summary>Holds <see cref="ExtractFileArchiveCommand"/> to paths the panel accepts and an archive name the module recognises.</summary>
public sealed class ExtractFileArchiveCommandValidator : AbstractValidator<ExtractFileArchiveCommand>
{
    /// <summary>Configures the field rules for <see cref="ExtractFileArchiveCommand"/>.</summary>
    public ExtractFileArchiveCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.ArchivePath)
            .Must(FilesPathRule.IsEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid))
            .Must(path => { return !FilesPathRule.IsReserved(path); })
            .WithMessage(nameof(ErrorMessages.FilesPathReserved))
            .Must(path => { return ArchiveFormatPolicy.TryFromName(FilesPathSegmentsPolicy.NameOf(path), out _); })
            .WithMessage(nameof(ErrorMessages.FilesArchiveFormatUnknown));

        RuleFor(command => command.DestinationDirectory)
            .Must(FilesPathRule.IsWellFormed)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid))
            .Must(path => { return !FilesPathRule.IsReserved(path); })
            .WithMessage(nameof(ErrorMessages.FilesPathReserved));
    }
}
