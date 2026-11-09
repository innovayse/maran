using FluentValidation;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;

namespace Maran.Modules.Files.Commands.RestoreTrashEntries;

/// <summary>Holds <see cref="RestoreTrashEntriesCommand"/> to a non-empty list of well-formed trash ids.</summary>
/// <remarks>
/// An id names a directory in the trash, so one shaped like a path is the thing this rule and the
/// agent's own exist to refuse.
/// </remarks>
public sealed class RestoreTrashEntriesCommandValidator : AbstractValidator<RestoreTrashEntriesCommand>
{
    /// <summary>Configures the field rules for <see cref="RestoreTrashEntriesCommand"/>.</summary>
    public RestoreTrashEntriesCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.TrashIds)
            .NotEmpty()
            .WithMessage(nameof(ErrorMessages.FilesBatchEmpty))
            .Must(ids => { return ids.All(FilesTrashIdRule.IsValid); })
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid));
    }
}
