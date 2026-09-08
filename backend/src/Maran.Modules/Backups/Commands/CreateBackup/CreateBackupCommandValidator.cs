using FluentValidation;

namespace Maran.Modules.Backups.Commands.CreateBackup;

/// <summary>
/// Validates <see cref="CreateBackupCommand"/> before it reaches the handler (rules/security.md
/// "Input").
/// </summary>
/// <remarks>
/// There is one field to check and one thing to say about it: an empty account id is a malformed
/// request, and it is refused here rather than being allowed to reach the account directory as a
/// lookup that would answer "not found" — a caller who sent nothing has made a different mistake
/// from one who named an account that is not theirs, and the two deserve different answers.
///
/// Everything that follows — whether the account exists, whether it is the caller's, and whether its
/// system user name is one the agent will accept — is decided by the handler and re-validated inside
/// the agent. The API's validation never substitutes for the agent's own boundary check
/// (rules/architecture.md "Agent").
/// </remarks>
public sealed class CreateBackupCommandValidator : AbstractValidator<CreateBackupCommand>
{
    /// <summary>Configures the field rules for <see cref="CreateBackupCommand"/>.</summary>
    public CreateBackupCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();
    }
}
