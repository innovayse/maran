using FluentValidation;

namespace Maran.Modules.Backups.Commands.VerifyBackup;

/// <summary>
/// Validates <see cref="VerifyBackupCommand"/> before it reaches the handler (rules/security.md
/// "Input").
/// </summary>
/// <remarks>
/// The route constraint already refuses anything that is not a GUID, so the only value this can still
/// refuse is the empty one a command built elsewhere could carry. Whether the backup exists, and is
/// the caller's, is the handler's tenant-scoped read and not a shape question.
/// </remarks>
public sealed class VerifyBackupCommandValidator : AbstractValidator<VerifyBackupCommand>
{
    /// <summary>Configures the field rules for <see cref="VerifyBackupCommand"/>.</summary>
    public VerifyBackupCommandValidator()
    {
        RuleFor(command => command.BackupId)
            .NotEmpty();
    }
}
