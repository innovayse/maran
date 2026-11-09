using FluentValidation;
using Maran.Modules.Files.Domain.Policies;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.CreateFileArchive;

/// <summary>Holds <see cref="CreateFileArchiveCommand"/> to paths the panel accepts and an archive name that matches its format.</summary>
public sealed class CreateFileArchiveCommandValidator : AbstractValidator<CreateFileArchiveCommand>
{
    /// <summary>Configures the field rules for <see cref="CreateFileArchiveCommand"/>.</summary>
    public CreateFileArchiveCommandValidator()
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

        RuleFor(command => command.DestinationPath)
            .Must(FilesPathRule.IsEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid))
            .Must(path => { return !FilesPathRule.IsReserved(path); })
            .WithMessage(nameof(ErrorMessages.FilesPathReserved));

        RuleFor(command => command.Format)
            .IsInEnum()
            .WithMessage(nameof(ErrorMessages.FilesArchiveFormatUnknown));

        // The name states the format it holds, so a ".zip" is never a gzipped tar and a download shows what it is.
        RuleFor(command => command)
            .Must(command => { return command.DestinationPath.EndsWith(ArchiveFormatPolicy.SuffixOf(command.Format), StringComparison.OrdinalIgnoreCase); })
            .When(command => { return command.DestinationPath is not null && Enum.IsDefined(command.Format); })
            .WithMessage(nameof(ErrorMessages.FilesArchiveFormatUnknown))
            .OverridePropertyName(nameof(CreateFileArchiveCommand.DestinationPath));
    }
}
