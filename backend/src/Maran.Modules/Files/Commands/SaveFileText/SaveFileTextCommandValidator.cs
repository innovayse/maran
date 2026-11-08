using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.SaveFileText;

/// <summary>Validates <see cref="SaveFileTextCommand"/> before it reaches the handler (rules/security.md "Input").</summary>
/// <remarks>
/// The size of the content is not checked here: the limit is configuration the handler reads at the call,
/// and the handler answers it with the same <c>FilesNotEditable</c> the read does.
/// </remarks>
public sealed class SaveFileTextCommandValidator : AbstractValidator<SaveFileTextCommand>
{
    /// <summary>Configures the rules.</summary>
    public SaveFileTextCommandValidator()
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

        // An empty file is a legitimate save; an absent body is not.
        RuleFor(command => command.Content)
            .NotNull();
    }
}
