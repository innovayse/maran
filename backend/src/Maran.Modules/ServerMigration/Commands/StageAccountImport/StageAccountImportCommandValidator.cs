using FluentValidation;
using Maran.SharedKernel.Utilities.Signatures;

namespace Maran.Modules.ServerMigration.Commands.StageAccountImport;

/// <summary>Checks the fingerprint the administrator typed has the shape of one, before any byte of a package is read.</summary>
public sealed class StageAccountImportCommandValidator : AbstractValidator<StageAccountImportCommand>
{
    /// <summary>Declares the rules.</summary>
    public StageAccountImportCommandValidator()
    {
        RuleFor(command => command.ExpectedKeyFingerprint)
            .NotEmpty()
            .Length(PublicKeyFingerprint.Length)
            .Matches("^[0-9a-f]{64}$");
    }
}
