using FluentValidation;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Commands.RestoreBackup;

/// <summary>
/// Validates <see cref="RestoreBackupCommand"/> before it reaches the handler (rules/security.md
/// "Input").
/// </summary>
/// <remarks>
/// <para>
/// <b>What this validator can and cannot decide.</b> It can say that a confirmation was supplied and
/// that it is shaped like a Linux user name at all; it cannot say whether it MATCHES, because the
/// account has not been resolved yet and resolving it is the handler's tenant-scoped read. So the
/// match is the handler's check and this is only the shape — and the split is worth stating, because
/// a reader who saw a confirmation rule here could reasonably assume the confirmation was fully
/// checked by the time the handler runs.
/// </para>
/// <para>
/// The alphabet is refused rather than escaped: the confirmation is compared, never interpolated,
/// but it is also the value an operator will read back out of a refusal, and a control character or
/// a newline in it would be a value the panel had accepted into its own diagnostics. Everything the
/// restore actually acts on — the account's real user name, the artifact's id — is re-validated
/// inside the agent regardless (rules/architecture.md "Agent").
/// </para>
/// <para>
/// Each message is a bare resx key, not an English sentence. <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric, and then resolves it as an error code
/// against the module's resources; an English sentence is silently discarded and the customer gets
/// the generic failure instead — which on this operation would mean a customer told "invalid
/// request" when what they need to be told is that they must type the account name.
/// </para>
/// </remarks>
public sealed class RestoreBackupCommandValidator : AbstractValidator<RestoreBackupCommand>
{
    /// <summary>The alphabet a Linux user name is accepted in here.</summary>
    /// <remarks>
    /// Anchored with <c>\z</c> rather than <c>$</c>: in .NET <c>$</c> also matches immediately
    /// before a trailing newline, so <c>alice\n</c> would satisfy a <c>$</c>-anchored pattern.
    /// </remarks>
    private const string UsernamePattern = @"\A[a-z_][a-z0-9_-]*\z";

    /// <summary>The longest confirmation accepted, matching the ceiling on a system user name.</summary>
    private const int MaximumUsernameLength = 32;

    /// <summary>Configures the field rules for <see cref="RestoreBackupCommand"/>.</summary>
    public RestoreBackupCommandValidator()
    {
        RuleFor(command => command.BackupId)
            .NotEmpty();

        RuleFor(command => command.ConfirmAccountUsername)
            .NotEmpty()
            .MaximumLength(MaximumUsernameLength)
            .Matches(UsernamePattern)
            .WithMessage(nameof(ErrorMessages.RestoreConfirmationRequired));
    }
}
