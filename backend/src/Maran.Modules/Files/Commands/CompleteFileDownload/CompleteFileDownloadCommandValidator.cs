using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.CompleteFileDownload;

/// <summary>Validates <see cref="CompleteFileDownloadCommand"/> before it reaches the handler.</summary>
public sealed class CompleteFileDownloadCommandValidator : AbstractValidator<CompleteFileDownloadCommand>
{
    /// <summary>Configures the rules.</summary>
    public CompleteFileDownloadCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Path)
            .Must(FilesPathRule.IsEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid));
    }
}
