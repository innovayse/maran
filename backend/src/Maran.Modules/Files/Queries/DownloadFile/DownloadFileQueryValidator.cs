using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Queries.DownloadFile;

/// <summary>Validates <see cref="DownloadFileQuery"/> before it reaches the handler (rules/security.md "Input").</summary>
public sealed class DownloadFileQueryValidator : AbstractValidator<DownloadFileQuery>
{
    /// <summary>Configures the rules.</summary>
    public DownloadFileQueryValidator()
    {
        RuleFor(query => query.AccountId)
            .NotEmpty();

        RuleFor(query => query.Path)
            .Must(FilesPathRule.IsEntryPath)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid));

        // The trash directory is reached through the trash routes and no other.
        RuleFor(query => query.Path)
            .Must(path => { return !FilesPathRule.IsReserved(path); })
            .WithMessage(nameof(ErrorMessages.FilesPathReserved));
    }
}
