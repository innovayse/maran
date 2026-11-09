using FluentValidation;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Commands.SetBackupDestinationEncryption;

/// <summary>Checks the part of the request the caller supplies and the server cannot establish.</summary>
/// <remarks>
/// Only the route id is checked, because <c>Enabled</c> is a <c>bool</c> and has no invalid value. An
/// empty id means the action never stamped one, which is a defect in the action and is refused here
/// rather than looked up.
/// </remarks>
public sealed class SetBackupDestinationEncryptionCommandValidator
    : AbstractValidator<SetBackupDestinationEncryptionCommand>
{
    /// <summary>Declares the rules.</summary>
    public SetBackupDestinationEncryptionCommandValidator()
    {
        RuleFor(command => command.DestinationId)
            .NotEmpty()
            .WithMessage(nameof(ErrorMessages.BackupDestinationNotFound));
    }
}
