using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.AbortFileDownload;

/// <summary>Validates <see cref="AbortFileDownloadCommand"/> before it reaches the handler.</summary>
public sealed class AbortFileDownloadCommandValidator : AbstractValidator<AbortFileDownloadCommand>
{
    /// <summary>Configures the rules.</summary>
    public AbortFileDownloadCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Path)
            .Must(FilesPathRule.IsEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid));
    }
}
