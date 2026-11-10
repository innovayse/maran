using FluentValidation;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Resources;

namespace Maran.Modules.ServerMigration.Commands.ConfirmRemoteServerHostKey;

/// <summary>
/// Holds <see cref="ConfirmRemoteServerHostKeyCommand"/> to the shape of a host key fingerprint
/// (rules/security.md "Input"). Whether it is one of the fingerprints the server offered is the handler's question.
/// </summary>
public sealed class ConfirmRemoteServerHostKeyCommandValidator : AbstractValidator<ConfirmRemoteServerHostKeyCommand>
{
    /// <summary>Declares the rule.</summary>
    public ConfirmRemoteServerHostKeyCommandValidator()
    {
        RuleFor(command => command.Fingerprint)
            .Must(HostKeyFingerprintPolicy.IsWellFormed)
            .WithErrorCode(nameof(ValidationMessages.FingerprintInvalid))
            .WithMessage(nameof(ValidationMessages.FingerprintInvalid));
    }
}
