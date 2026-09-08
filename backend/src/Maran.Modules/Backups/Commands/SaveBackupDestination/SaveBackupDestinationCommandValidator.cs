using FluentValidation;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Commands.SaveBackupDestination;

/// <summary>
/// Validates <see cref="SaveBackupDestinationCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// It checks the SHAPE of the request only. Whether this build can act on the kind that was named is
/// not a shape question — <c>S3</c> is a perfectly well-formed value of a real enum — so it is
/// answered by the handler through <c>RemoteDestinationPolicy</c>, which is the panel's one statement
/// of it. Answering it here as well would be a second copy of a refusal.
///
/// Each message is a bare resx key, not an English sentence: <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric and then resolves it as an error code
/// against this module's resources.
/// </remarks>
public sealed class SaveBackupDestinationCommandValidator : AbstractValidator<SaveBackupDestinationCommand>
{
    /// <summary>Configures the field rules for <see cref="SaveBackupDestinationCommand"/>.</summary>
    public SaveBackupDestinationCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(BackupDestination.NameMaxLength)
            .WithMessage(nameof(ErrorMessages.BackupDestinationNameInvalid));

        RuleFor(command => command.Kind)
            .IsInEnum();
    }
}
