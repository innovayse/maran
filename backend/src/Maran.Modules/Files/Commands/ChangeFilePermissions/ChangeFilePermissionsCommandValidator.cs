using FluentValidation;
using Maran.Modules.Files.Domain.Policies;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.ChangeFilePermissions;

/// <summary>Holds <see cref="ChangeFilePermissionsCommand"/> to a path and to plain permission bits.</summary>
/// <remarks>
/// The mode, and the folders' mode when one is given, are three octal digits. A folder mode of zero is
/// refused because the agent reads zero as "same as the mode". A fourth digit, which is where setuid, setgid
/// and the sticky bit would be written, is refused here and again by the agent.
/// </remarks>
public sealed class ChangeFilePermissionsCommandValidator : AbstractValidator<ChangeFilePermissionsCommand>
{
    /// <summary>Configures the field rules for <see cref="ChangeFilePermissionsCommand"/>.</summary>
    public ChangeFilePermissionsCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Path)
            .Must(FilesPathRule.IsEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid))
            .Must(path => { return !FilesPathRule.IsReserved(path); })
            .WithMessage(nameof(ErrorMessages.FilesPathReserved));

        RuleFor(command => command.Mode)
            .Must(mode => { return FilesModePolicy.TryParse(mode, out _); })
            .WithMessage(nameof(ErrorMessages.FilesModeInvalid));

        RuleFor(command => command.DirectoryMode)
            .Must(directoryMode => { return FilesModePolicy.TryParseFolderMode(directoryMode, out _); })
            .WithMessage(nameof(ErrorMessages.FilesModeInvalid))
            .When(command => { return command.DirectoryMode is not null; });
    }
}
