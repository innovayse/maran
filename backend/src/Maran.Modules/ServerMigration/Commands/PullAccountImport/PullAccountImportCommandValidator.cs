using FluentValidation;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Utilities.Network;
using Maran.SharedKernel.Utilities.Signatures;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.ServerMigration.Commands.PullAccountImport;

/// <summary>Checks the address, the grant and the fingerprint have the shape of one before anything leaves the server.</summary>
public sealed class PullAccountImportCommandValidator : AbstractValidator<PullAccountImportCommand>
{
    /// <summary>Declares the rules.</summary>
    public PullAccountImportCommandValidator()
    {
        RuleFor(command => command.SourceUrl)
            .Must(url =>
            {
                return PublicUrlRule.TryNormalise(url, out _);
            })
            .WithErrorCode(nameof(ValidationMessages.MigrationPullUrlInvalid))
            .WithMessage(nameof(ValidationMessages.MigrationPullUrlInvalid));

        RuleFor(command => command.Grant)
            .Must(grant =>
            {
                return grant is not null && grant.Reveal().Length == ExportGrantTokenHasher.TokenLength;
            })
            .WithErrorCode(nameof(ValidationMessages.MigrationPullGrantInvalid))
            .WithMessage(nameof(ValidationMessages.MigrationPullGrantInvalid));

        RuleFor(command => command.ExpectedKeyFingerprint)
            .NotEmpty()
            .Length(PublicKeyFingerprint.Length)
            .Matches("^[0-9a-f]{64}$");
    }
}
