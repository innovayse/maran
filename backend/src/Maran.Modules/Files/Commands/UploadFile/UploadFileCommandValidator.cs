using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.UploadFile;

/// <summary>Validates <see cref="UploadFileCommand"/> before it reaches the handler (rules/security.md "Input").</summary>
/// <remarks>
/// The size is not checked here: the cap is configuration the handler reads at the call, and the body
/// is counted as it is read, so no number a client sends decides it.
/// </remarks>
public sealed class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    /// <summary>Configures the rules.</summary>
    public UploadFileCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Path)
            .Must(FilesPathRule.IsEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid));

        // The trash directory is reached through the trash routes and no other.
        RuleFor(command => command.Path)
            .Must(path => { return !FilesPathRule.IsReserved(path); })
            .WithMessage(nameof(ErrorMessages.FilesPathReserved));
    }
}
